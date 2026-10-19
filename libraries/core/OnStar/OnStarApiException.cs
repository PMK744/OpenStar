namespace OpenStar.Core.OnStar;

/// <summary>
/// A GM API call failed. Carries the upstream status and body so callers can
/// pass them through to API clients.
/// </summary>
public sealed class OnStarApiException(string message, int? statusCode = null, string? responseBody = null)
    : Exception(message)
{
    public int? StatusCode { get; } = statusCode;

    public string? ResponseBody { get; } = responseBody;
}
