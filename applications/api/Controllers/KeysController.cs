using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OpenStar.Api.Authentication;
using OpenStar.Core.Security;

namespace OpenStar.Api.Controllers;

[ApiController]
[Route("api/keys")]
[Authorize(Policy = Policies.Admin)]
public sealed class KeysController(ApiKeyManager apiKeyManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeySummary>>> List(CancellationToken cancellationToken)
    {
        return Ok(await apiKeyManager.ListAsync(cancellationToken));
    }

    /// <summary>Creates a client key. The secret is only returned here.</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedApiKey>> Create(CreateKeyRequest request, CancellationToken cancellationToken)
    {
        return Ok(await apiKeyManager.CreateAsync(request.Name, cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken cancellationToken)
    {
        return await apiKeyManager.RevokeAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}

public sealed record CreateKeyRequest(string Name);
