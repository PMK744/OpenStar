using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenStar.Core.Authentication;

/// <summary>
/// Talks to GM's B2C tenant (PKCE login, refresh) and to the GM token
/// exchange that turns a Microsoft access token into a GM API token.
/// </summary>
public sealed class GmAuthClient(HttpClient httpClient, IOptions<OpenStarOptions> options, ILogger<GmAuthClient> logger)
{
    private OpenIdConfiguration? discovery;

    public async Task<PendingLogin> CreateLoginAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = await GetEndpointsAsync(cancellationToken);
        var codeVerifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = GmAuthConstants.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = GmAuthConstants.RedirectUri,
            ["scope"] = GmAuthConstants.Scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            // Always show the sign-in form. With an existing GM session B2C would
            // redirect straight to the app scheme, leaving the tab on about:blank.
            ["prompt"] = "login",
            ["bundleID"] = "com.gm.myChevrolet",
            ["mode"] = "dark",
            ["evar25"] = "mobile_mychevrolet_chevrolet_us_app_launcher_sign_in_or_create_account",
            ["channel"] = "lightreg",
            ["ui_locales"] = "en-US",
            ["brand"] = "chevrolet",
        };

        var queryString = string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        return new PendingLogin
        {
            State = state,
            CodeVerifier = codeVerifier,
            AuthorizationUrl = $"{endpoints.AuthorizationEndpoint}?{queryString}",
            TokenEndpoint = endpoints.TokenEndpoint!,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        };
    }

    public Task<MicrosoftTokenSet> RedeemCodeAsync(PendingLogin login, string code, CancellationToken cancellationToken = default)
    {
        return RequestMicrosoftTokenAsync(login.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = GmAuthConstants.ClientId,
            ["redirect_uri"] = GmAuthConstants.RedirectUri,
            ["code"] = code,
            ["code_verifier"] = login.CodeVerifier,
        }, previous: null, cancellationToken);
    }

    public async Task<MicrosoftTokenSet> RefreshAsync(MicrosoftTokenSet current, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            throw new OpenStarAuthException("The linked account has no refresh token. Sign in again.");
        }

        var endpoints = await GetEndpointsAsync(cancellationToken);
        return await RequestMicrosoftTokenAsync(endpoints.TokenEndpoint!, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = GmAuthConstants.ClientId,
            ["redirect_uri"] = GmAuthConstants.RedirectUri,
            ["scope"] = GmAuthConstants.Scope,
            ["refresh_token"] = current.RefreshToken,
        }, current, cancellationToken);
    }

    public async Task<GmTokenSet> ExchangeForGmTokenAsync(
        MicrosoftTokenSet microsoftTokens,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.ServiceUrl.TrimEnd('/') + GmAuthConstants.GmTokenPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = microsoftTokens.AccessToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["scope"] = GmAuthConstants.GmTokenScope,
                ["device_id"] = deviceId,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd(GmAuthConstants.UserAgent);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GM token exchange failed with {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new OpenStarAuthException($"GM token exchange failed ({(int)response.StatusCode}).");
        }

        var token = JsonSerializer.Deserialize<GmTokenResponse>(body);
        if (string.IsNullOrEmpty(token?.AccessToken))
        {
            throw new OpenStarAuthException("GM token exchange returned no access token.");
        }

        var vins = new List<string>();
        if (Jwt.ReadPayload(token.AccessToken) is { } payload
            && payload.TryGetProperty("vehs", out var vehicles)
            && vehicles.ValueKind == JsonValueKind.Array)
        {
            foreach (var vehicle in vehicles.EnumerateArray())
            {
                if (vehicle.TryGetProperty("vin", out var vin) && vin.GetString() is { } value)
                {
                    vins.Add(value.ToUpperInvariant());
                }
            }
        }

        return new GmTokenSet
        {
            AccessToken = token.AccessToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn ?? 1800),
            AccountNumber = token.AccountInfo?.AccountNumber,
            Vins = vins,
        };
    }

    private async Task<MicrosoftTokenSet> RequestMicrosoftTokenAsync(
        string tokenEndpoint,
        Dictionary<string, string> form,
        MicrosoftTokenSet? previous,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<B2CTokenResponse>(cancellationToken);

        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(token?.AccessToken))
        {
            var detail = token?.ErrorDescription ?? token?.Error ?? $"HTTP {(int)response.StatusCode}";
            logger.LogWarning("GM B2C token request ({GrantType}) failed: {Detail}", form["grant_type"], detail);
            throw new OpenStarAuthException($"GM sign-in token request failed: {FirstLine(detail)}");
        }

        var now = DateTimeOffset.UtcNow;
        return new MicrosoftTokenSet
        {
            AccessToken = token.AccessToken,
            // B2C rotates refresh tokens; keep the old one if a new one wasn't issued.
            RefreshToken = token.RefreshToken ?? previous?.RefreshToken,
            IdToken = token.IdToken ?? previous?.IdToken,
            ExpiresAt = now.AddSeconds(token.ExpiresIn ?? 3600),
            RefreshTokenExpiresAt = token.RefreshTokenExpiresIn is { } seconds
                ? now.AddSeconds(seconds)
                : previous?.RefreshTokenExpiresAt,
            ObtainedAt = now,
        };
    }

    private async Task<OpenIdConfiguration> GetEndpointsAsync(CancellationToken cancellationToken)
    {
        if (discovery is not null)
        {
            return discovery;
        }

        try
        {
            var discovered = await httpClient.GetFromJsonAsync<OpenIdConfiguration>(GmAuthConstants.DiscoveryUrl, cancellationToken);
            if (discovered is { AuthorizationEndpoint: not null, TokenEndpoint: not null })
            {
                return discovery = discovered;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "GM OpenID discovery failed; using fallback endpoints");
        }

        // Don't cache the fallback so the next call retries discovery.
        return new OpenIdConfiguration
        {
            AuthorizationEndpoint = GmAuthConstants.FallbackAuthorizationEndpoint,
            TokenEndpoint = GmAuthConstants.FallbackTokenEndpoint,
        };
    }

    private static string FirstLine(string value)
    {
        var line = value.Split('\n', 2)[0].Trim();
        return line.Length > 200 ? line[..200] : line;
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
