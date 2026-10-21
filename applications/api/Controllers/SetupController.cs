using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OpenStar.Api.Authentication;
using OpenStar.Core.Authentication;

namespace OpenStar.Api.Controllers;

/// <summary>
/// Links the GM account. The browser signs in on GM's own page (helped by the
/// login extension) and hands back the app redirect it ends on.
/// </summary>
[ApiController]
[Route("api/setup")]
[Authorize(Policy = Policies.Admin)]
public sealed class SetupController(AccountManager accountManager) : ControllerBase
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
        return Ok(await accountManager.StartLoginAsync(cancellationToken));
    }

    /// <summary>Redeems the redirect (or code) captured after GM sign-in.</summary>
    [HttpPost("login/complete")]
    public async Task<ActionResult<AccountStatus>> CompleteLogin(CompleteLoginRequest request, CancellationToken cancellationToken)
    {
        return Ok(await accountManager.CompleteLoginAsync(request.State, request.Redirect, cancellationToken));
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
