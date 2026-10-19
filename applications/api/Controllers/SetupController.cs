using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using OpenStar.Api.Authentication;
using OpenStar.Core;
using OpenStar.Core.Authentication;
using OpenStar.Core.BrowserLogin;

namespace OpenStar.Api.Controllers;

/// <summary>
/// Links the GM account. GM has no third-party OAuth, so OpenStar signs in
/// through a real browser using credentials the admin enters here.
/// </summary>
[ApiController]
[Route("api/setup")]
[Authorize(Policy = Policies.Admin)]
public sealed class SetupController(
    AccountManager accountManager,
    GmBrowserLogin browserLogin,
    IOptions<OpenStarOptions> options) : ControllerBase
{
    /// <summary>Whether a GM account is linked. Open so the UI can show first-run state.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Status(CancellationToken cancellationToken)
    {
        var status = await accountManager.GetStatusAsync(cancellationToken);
        return Ok(new { status.IsLinked });
    }

    /// <summary>Full account details for the admin.</summary>
    [HttpGet("account")]
    public async Task<ActionResult<AccountStatus>> Account(CancellationToken cancellationToken)
    {
        return Ok(await accountManager.GetStatusAsync(cancellationToken));
    }

    /// <summary>Creates a PKCE login and returns the GM sign-in URL to open.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginStart>> StartLogin(CancellationToken cancellationToken)
    {
        var login = await accountManager.StartLoginAsync(cancellationToken);
        return Ok(login with { RelayUrl = BuildRelayUrl(login.AuthorizationUrl) });
    }

    /// <summary>
    /// Points the GM authorize URL at the relay so the browser signs in through
    /// OpenStar. Returns null when the relay is disabled.
    /// </summary>
    private string? BuildRelayUrl(string authorizationUrl)
    {
        if (options.Value.LoginRelayPort <= 0)
        {
            return null;
        }

        var authorize = new Uri(authorizationUrl);
        var relayBase = options.Value.LoginRelayUrl?.TrimEnd('/')
            ?? $"{Request.Scheme}://{Request.Host.Host}:{options.Value.LoginRelayPort}";

        return $"{relayBase}{authorize.PathAndQuery}";
    }

    /// <summary>Redeems the redirect (or code) captured after GM sign-in.</summary>
    [HttpPost("login/complete")]
    public async Task<ActionResult<AccountStatus>> CompleteLogin(CompleteLoginRequest request, CancellationToken cancellationToken)
    {
        return Ok(await accountManager.CompleteLoginAsync(request.State, request.Redirect, cancellationToken));
    }

    /// <summary>
    /// Signs in to GM in a server-side browser with the given credentials. The
    /// result says whether the account linked or a verification code is needed.
    /// </summary>
    [HttpPost("browser-login")]
    public async Task<ActionResult<BrowserLoginResult>> BrowserLogin(BrowserLoginRequest request, CancellationToken cancellationToken)
    {
        return Ok(await browserLogin.StartAsync(request.Email, request.Password, cancellationToken));
    }

    /// <summary>Supplies the MFA code to a browser sign-in waiting for one.</summary>
    [HttpPost("browser-login/code")]
    public async Task<ActionResult<BrowserLoginResult>> BrowserLoginCode(BrowserLoginCodeRequest request, CancellationToken cancellationToken)
    {
        return Ok(await browserLogin.SubmitCodeAsync(request.State, request.Code, cancellationToken));
    }

    /// <summary>Abandons a browser sign-in and closes its browser.</summary>
    [HttpPost("browser-login/cancel")]
    public async Task<IActionResult> BrowserLoginCancel(BrowserLoginCodeRequest request)
    {
        await browserLogin.CancelAsync(request.State);
        return NoContent();
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AccountStatus>> Refresh(CancellationToken cancellationToken)
    {
        await accountManager.RefreshAsync(forceMicrosoftRefresh: true, cancellationToken);
        return Ok(await accountManager.GetStatusAsync(cancellationToken));
    }

    [HttpDelete("account")]
    public async Task<IActionResult> Unlink(CancellationToken cancellationToken)
    {
        await accountManager.UnlinkAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Lets the UI check an entered admin key.</summary>
    [HttpGet("verify")]
    public IActionResult Verify() => NoContent();
}

public sealed record CompleteLoginRequest(string State, string Redirect);

public sealed record BrowserLoginRequest(string Email, string Password);

public sealed record BrowserLoginCodeRequest(string State, string Code);
