using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenStar.Core.Authentication;

namespace OpenStar.Core.OnStar;

/// <summary>
/// Calls the GM mobile API with the linked account's token. Responses are
/// returned as raw JSON so new GM fields reach API clients without changes here.
/// </summary>
public sealed class OnStarClient(
    HttpClient httpClient,
    AccountManager accountManager,
    IOptions<OpenStarOptions> options,
    ILogger<OnStarClient> logger)
{
    private const string GarageQuery =
        "query getVehiclesMBFF {vehicles {vin vehicleId make model nickName year imageUrl onstarCapable vehicleType roleCode onstarStatusCode onstarAccountNumber preDelivery orderNum orderStatus}}";

    // Remembers vehicles whose action commands only work on the v1 API.
    private readonly HashSet<string> v1OnlyVins = [];

    private string ServiceUrl => options.Value.ServiceUrl.TrimEnd('/');

    public Task<JsonElement> GetGarageAsync(CancellationToken cancellationToken = default)
    {
        return GraphQlAsync(GarageQuery, cancellationToken);
    }

    public Task<JsonElement> GetVehicleDetailsAsync(string vin, CancellationToken cancellationToken = default)
    {
        var query =
            "query getMbffGarageVehicleDetails {" +
            $"vehicleDetails(vin: \"{SanitizeVin(vin)}\") {{" +
            "vin make model year onstarCapable imageUrl rpoCodes orderDate " +
            "permissions { userPermissions accountPermissions { acctNum permissions disabledPermissionsResponse { code reason } } devicePermissions { id permissions disabledPermissions { code reason } } } " +
            "vehicleCommands { name url serviceId isEligible inEligibleReason metaData { supportedDiagnostics } } " +
            "color { exteriorColor interiorTrimColor } " +
            "vehicleMetaData { propulsionAndFuelType { fuelCategory propulsionType } unit { unitGen unitGenDescription } configuredCountry bodyStyle features extColor } " +
            "onstarInfo { onStarStatus ownerAccount isShared associatedDate }" +
            "}}";

        return GraphQlAsync(query, cancellationToken);
    }

    public Task<JsonElement> GetDiagnosticsAsync(string vin, CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, $"{ServiceUrl}/api/v1/vh/vehiclehealth/v1/healthstatus/{SanitizeVin(vin)}", null, cancellationToken);
    }

    /// <summary>
    /// Requests a fresh location fix and waits until the vehicle reports it
    /// (or the polling timeout passes), returning the digital twin payload.
    /// </summary>
    public async Task<JsonElement> GetLocationAsync(string vin, CancellationToken cancellationToken = default)
    {
        var url = $"{ServiceUrl}/veh/datadelivery/digitaltwin/v1/vehicles/{SanitizeVin(vin)}";
        var result = await SendAsync(HttpMethod.Get, $"{url}?sms=true&region=na", null, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.Value.CommandPollingTimeoutSeconds);

        while (IsLocationPending(result) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Value.CommandPollingIntervalSeconds), cancellationToken);
            result = await SendAsync(HttpMethod.Get, $"{url}?sms=false&region=na", null, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Submits a command, trying the v3 API first and falling back to v1 when
    /// GM rejects the v3 request as unsupported for the vehicle.
    /// </summary>
    public async Task<CommandProgress> SubmitCommandAsync(
        string vin,
        VehicleCommand command,
        CommandOptions commandOptions,
        CancellationToken cancellationToken = default)
    {
        vin = SanitizeVin(vin);
        var definition = CommandDefinition.For(command);
        var v1Url = $"{ServiceUrl}/api/v1/account/vehicles/{vin}/commands/{definition.V1Command}";

        if (!IsV1Only(vin))
        {
            try
            {
                var v3Url = $"{ServiceUrl}/veh/cmd/v3/{definition.V3Path}/{vin}";
                return CommandProgress.Parse(await SendAsync(HttpMethod.Post, v3Url, definition.V3Body(commandOptions), cancellationToken));
            }
            catch (OnStarApiException exception) when (exception.StatusCode == (int)HttpStatusCode.BadRequest)
            {
                logger.LogInformation("v3 {Command} rejected for {Vin}; falling back to v1", command, vin);
            }
        }

        var progress = CommandProgress.Parse(await SendAsync(HttpMethod.Post, v1Url, definition.V1Body(commandOptions), cancellationToken));
        lock (v1OnlyVins)
        {
            v1OnlyVins.Add(vin);
        }

        return progress;
    }

    public async Task<CommandProgress> PollCommandAsync(string pollUrl, CancellationToken cancellationToken = default)
    {
        // Only follow poll URLs on the GM API host so a response can't redirect our token elsewhere.
        if (!Uri.TryCreate(pollUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, new Uri(ServiceUrl).Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new OnStarApiException($"Refusing to poll unexpected URL {pollUrl}.");
        }

        return CommandProgress.Parse(await SendAsync(HttpMethod.Get, pollUrl, null, cancellationToken));
    }

    public async Task EnsureVinAuthorizedAsync(string vin, CancellationToken cancellationToken = default)
    {
        vin = SanitizeVin(vin);
        var token = await accountManager.GetGmTokenAsync(cancellationToken);
        if (token.Vins.Count > 0 && !token.Vins.Contains(vin))
        {
            throw new OnStarApiException($"VIN {vin} is not on the linked OnStar account.", (int)HttpStatusCode.NotFound);
        }
    }

    private bool IsV1Only(string vin)
    {
        lock (v1OnlyVins)
        {
            return v1OnlyVins.Contains(vin);
        }
    }

    private async Task<JsonElement> GraphQlAsync(string query, CancellationToken cancellationToken)
    {
        var result = await SendAsync(HttpMethod.Post, $"{ServiceUrl}/mbff/garage/v1", query, cancellationToken);
        if (result.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            throw new OnStarApiException("GM garage query returned errors.", (int)HttpStatusCode.BadGateway, result.GetRawText());
        }

        return result;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        var token = await accountManager.GetGmTokenAsync(cancellationToken);
        using var response = await SendWithTokenAsync(method, url, body, token.AccessToken, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The cached token was revoked early; refresh once and retry.
            token = await accountManager.RefreshAsync(forceMicrosoftRefresh: false, cancellationToken);
            using var retry = await SendWithTokenAsync(method, url, body, token.AccessToken, cancellationToken);
            return await ReadAsync(retry, cancellationToken);
        }

        return await ReadAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(
        HttpMethod method,
        string url,
        object? body,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.AcceptLanguage.ParseAdd("en-US");
        request.Headers.UserAgent.ParseAdd(GmAuthConstants.UserAgent);
        request.Headers.TryAddWithoutValidation("appversion", GmAuthConstants.AppVersion);
        request.Headers.TryAddWithoutValidation("locale", "en-US");
        request.Headers.TryAddWithoutValidation("push-request", "allow");

        request.Content = body switch
        {
            string text => new StringContent(text, Encoding.UTF8, "text/plain"),
            JsonNode json => new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json"),
            null when method == HttpMethod.Post => new StringContent("{}", Encoding.UTF8, "application/json"),
            _ => null,
        };

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GM API {Method} {Url} returned {StatusCode}: {Body}",
                response.RequestMessage?.Method, response.RequestMessage?.RequestUri, (int)response.StatusCode, text);
            throw new OnStarApiException($"GM API returned {(int)response.StatusCode}.", (int)response.StatusCode, text);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return JsonSerializer.SerializeToElement(new { });
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new OnStarApiException("GM API returned a non-JSON response.", (int)HttpStatusCode.BadGateway, text);
        }
    }

    private static bool IsLocationPending(JsonElement result)
    {
        return result.TryGetProperty("telemetry", out var telemetry)
            && telemetry.TryGetProperty("data", out var data)
            && data.TryGetProperty("session", out var session)
            && session.TryGetProperty("updatePending", out var pending)
            && pending.GetString() == "PENDING";
    }

    private static string SanitizeVin(string vin)
    {
        var upper = vin.Trim().ToUpperInvariant();
        if (upper.Length != 17 || !upper.All(char.IsAsciiLetterOrDigit))
        {
            throw new OnStarApiException($"'{vin}' is not a valid VIN.", (int)HttpStatusCode.BadRequest);
        }

        return upper;
    }
}
