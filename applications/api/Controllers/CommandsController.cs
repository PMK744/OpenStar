using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OpenStar.Api.Authentication;
using OpenStar.Core.OnStar;

namespace OpenStar.Api.Controllers;

[ApiController]
[Route("api/commands")]
[Authorize(Policy = Policies.Client)]
public sealed class CommandsController(CommandTracker tracker) : ControllerBase
{
    /// <summary>Recently submitted commands, newest first.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<CommandRecord>> List()
    {
        return Ok(tracker.List());
    }

    [HttpGet("{id}")]
    public ActionResult<CommandRecord> Get(string id)
    {
        return tracker.Get(id) is { } record ? Ok(record) : NotFound();
    }
}
