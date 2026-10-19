using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using OpenStar.Api.Authentication;
using OpenStar.Core.OnStar;

namespace OpenStar.Api.Controllers;

[ApiController]
[Route("api/vehicles")]
[Authorize(Policy = Policies.Client)]
public sealed class VehiclesController(OnStarClient client, CommandTracker tracker) : ControllerBase
{
    /// <summary>Vehicles on the linked account (GM garage).</summary>
    [HttpGet]
    public async Task<ActionResult<JsonElement>> List(CancellationToken cancellationToken)
    {
        return Ok(await client.GetGarageAsync(cancellationToken));
    }

    [HttpGet("{vin}")]
    public async Task<ActionResult<JsonElement>> Details(string vin, CancellationToken cancellationToken)
    {
        await client.EnsureVinAuthorizedAsync(vin, cancellationToken);
        return Ok(await client.GetVehicleDetailsAsync(vin, cancellationToken));
    }

    /// <summary>Asks the vehicle for a fresh location and waits for it (up to the polling timeout).</summary>
    [HttpGet("{vin}/location")]
    public async Task<ActionResult<JsonElement>> Location(string vin, CancellationToken cancellationToken)
    {
        await client.EnsureVinAuthorizedAsync(vin, cancellationToken);
        return Ok(await client.GetLocationAsync(vin, cancellationToken));
    }

    [HttpGet("{vin}/diagnostics")]
    public async Task<ActionResult<JsonElement>> Diagnostics(string vin, CancellationToken cancellationToken)
    {
        await client.EnsureVinAuthorizedAsync(vin, cancellationToken);
        return Ok(await client.GetDiagnosticsAsync(vin, cancellationToken));
    }

    /// <summary>
    /// Sends a command. Returns 202 with a command record to poll at
    /// <c>/api/commands/{id}</c>, or with <c>?wait=true</c> blocks until GM
    /// reports the outcome (200) or the wait times out (202).
    /// </summary>
    [HttpPost("{vin}/commands/{command}")]
    public async Task<ActionResult<CommandRecord>> Command(
        string vin,
        string command,
        [FromBody] CommandOptions? options,
        [FromQuery] bool wait = false,
        CancellationToken cancellationToken = default)
    {
        if (!CommandNames.TryParse(command, out var vehicleCommand))
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Unknown command",
                detail: $"Supported commands: {string.Join(", ", CommandNames.All)}.");
        }

        var record = await tracker.StartAsync(vin, vehicleCommand, options ?? new CommandOptions(), cancellationToken);
        if (wait)
        {
            await tracker.WaitAsync(record, TimeSpan.FromSeconds(120), cancellationToken);
        }

        return record.Status == CommandStatus.InProgress
            ? AcceptedAtAction(nameof(CommandsController.Get), "Commands", new { id = record.Id }, record)
            : Ok(record);
    }
}

/// <summary>
/// URL names for commands (kebab-case, e.g. <c>cancel-start</c>).
/// </summary>
internal static class CommandNames
{
    public static IEnumerable<string> All => Enum.GetValues<VehicleCommand>().Select(ToName);

    public static bool TryParse(string name, out VehicleCommand command)
    {
        command = Enum.GetValues<VehicleCommand>().FirstOrDefault(value => ToName(value) == name.ToLowerInvariant());
        return ToName(command) == name.ToLowerInvariant();
    }

    private static string ToName(VehicleCommand command) => JsonNamingPolicy.KebabCaseLower.ConvertName(command.ToString());
}
