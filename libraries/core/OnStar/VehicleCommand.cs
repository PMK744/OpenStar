using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenStar.Core.OnStar;

[JsonConverter(typeof(JsonStringEnumConverter<VehicleCommand>))]
public enum VehicleCommand
{
    Lock,
    Unlock,
    Start,
    CancelStart,
    LockTrunk,
    UnlockTrunk,
    Alert,
    CancelAlert,
    FlashLights,
    StopLights,
}

/// <summary>
/// Optional parameters accepted by vehicle commands. Each command reads only
/// the fields that apply to it.
/// </summary>
public sealed class CommandOptions
{
    /// <summary>Seconds to wait before the vehicle acts (lock, unlock, trunk, alert).</summary>
    public int? Delay { get; set; }

    /// <summary>Alert duration in minutes.</summary>
    public int? Duration { get; set; }

    /// <summary>Target cabin temperature in Celsius for remote start, where supported.</summary>
    public double? CabinTemperature { get; set; }

    /// <summary>Alert actions: <c>Honk</c>, <c>Flash</c>.</summary>
    public List<string>? Action { get; set; }

    /// <summary>Alert overrides: <c>DoorOpen</c>, <c>IgnitionOn</c>.</summary>
    public List<string>? Override { get; set; }
}

/// <summary>
/// Maps each command to the GM v3 route and its legacy v1 fallback, and
/// builds the request bodies each version expects (per OnStarJS).
/// </summary>
internal sealed record CommandDefinition(string V3Path, string V1Command, Func<CommandOptions, JsonObject?> V3Body, Func<CommandOptions, JsonObject?> V1Body)
{
    public static CommandDefinition For(VehicleCommand command) => command switch
    {
        VehicleCommand.Lock => Delayed("lock", "lock", "lockDoorRequest"),
        VehicleCommand.Unlock => Delayed("unlock", "unlock", "unlockDoorRequest"),
        VehicleCommand.LockTrunk => Delayed("lockTrunk", "lockTrunk", "lockTrunkRequest"),
        VehicleCommand.UnlockTrunk => Delayed("unlockTrunk", "unlockTrunk", "unlockTrunkRequest"),
        VehicleCommand.Start => new("start", "start", StartBody, StartBody),
        VehicleCommand.CancelStart => Empty("cancelStart", "cancelStart"),
        VehicleCommand.Alert => AlertCommand(["Honk", "Flash"]),
        VehicleCommand.FlashLights => AlertCommand(["Flash"]),
        VehicleCommand.CancelAlert or VehicleCommand.StopLights => Empty("cancelAlert", "cancelAlert"),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    private static CommandDefinition Empty(string v3Path, string v1Command) =>
        new(v3Path, v1Command, _ => null, _ => null);

    private static CommandDefinition Delayed(string v3Path, string v1Command, string wrapper)
    {
        JsonObject Body(CommandOptions options) => new()
        {
            [wrapper] = new JsonObject { ["delay"] = options.Delay ?? 0 },
        };

        return new(v3Path, v1Command, Body, Body);
    }

    private static JsonObject? StartBody(CommandOptions options) =>
        options.CabinTemperature is { } temperature
            ? new JsonObject { ["cabinTemperature"] = (int)Math.Round(temperature) }
            : null;

    private static CommandDefinition AlertCommand(string[] defaultActions)
    {
        // v3 takes the alert fields at the top level; v1 nests them under alertRequest.
        JsonObject V3(CommandOptions options) => new()
        {
            ["action"] = ToArray(options.Action ?? [.. defaultActions]),
            ["delay"] = options.Delay ?? 0,
            ["duration"] = options.Duration ?? 1,
            ["override"] = ToArray(options.Override ?? ["DoorOpen", "IgnitionOn"]),
        };

        return new("alert", "alert", V3, options => new JsonObject { ["alertRequest"] = V3(options) });
    }

    private static JsonArray ToArray(IEnumerable<string> values) =>
        new([.. values.Select(value => (JsonNode)JsonValue.Create(value))]);
}
