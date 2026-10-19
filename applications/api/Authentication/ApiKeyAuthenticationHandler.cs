using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using OpenStar.Core.Security;

namespace OpenStar.Api.Authentication;

/// <summary>
/// Authenticates requests by the key in <c>X-Api-Key</c> or <c>Authorization: Bearer</c>.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyManager apiKeyManager) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(key)
            && Request.Headers.Authorization.ToString() is var authorization
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = authorization["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.NoResult();
        }

        var role = await apiKeyManager.ValidateAsync(key, Context.RequestAborted);
        if (role is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role.Value.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class Policies
{
    /// <summary>Any valid key: vehicle data and commands.</summary>
    public const string Client = nameof(Client);

    /// <summary>The admin key: account linking and key management.</summary>
    public const string Admin = nameof(Admin);
}
