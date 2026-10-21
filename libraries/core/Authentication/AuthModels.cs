using System.Text.Json.Serialization;

namespace OpenStar.Core.Authentication;

/// <summary>
/// Microsoft (GM B2C) tokens obtained from the interactive login.
/// </summary>
public sealed class MicrosoftTokenSet
{
    public required string AccessToken { get; set; }

    public string? RefreshToken { get; set; }

    public string? IdToken { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    public DateTimeOffset ObtainedAt { get; set; }
}

/// <summary>
/// GM API bearer token issued by exchanging a Microsoft access token.
/// </summary>
public sealed class GmTokenSet
{
    public required string AccessToken { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string? AccountNumber { get; set; }

    public List<string> Vins { get; set; } = [];
}

public sealed class PendingLogin
{
    public required string State { get; init; }

    public required string CodeVerifier { get; init; }

    public required string AuthorizationUrl { get; init; }

    public required string TokenEndpoint { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed record LoginStart(string State, string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record AccountStatus(
    bool IsLinked,
    string? Email,
    string? AccountNumber,
    IReadOnlyList<string> Vins,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    string? LastError);

internal sealed class B2CTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    [JsonPropertyName("expires_in")]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token_expires_in")]
    public long? RefreshTokenExpiresIn { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

internal sealed class GmTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? ExpiresIn { get; set; }

    [JsonPropertyName("onstar_account_info")]
    public GmAccountInfo? AccountInfo { get; set; }
}

internal sealed class GmAccountInfo
{
    [JsonPropertyName("account_no")]
    public string? AccountNumber { get; set; }
}

internal sealed class OpenIdConfiguration
{
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; set; }
}
