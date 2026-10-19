using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenStar.Core.OnStar;

[JsonConverter(typeof(JsonStringEnumConverter<CommandStatus>))]
public enum CommandStatus
{
    InProgress,
    Success,
    Failure,
    Timeout,
}

/// <summary>
/// One snapshot of a GM command, normalized across the v3 shape
/// (<c>{ requestId, status, url }</c>) and the v1 shape (<c>{ commandResponse: { … } }</c>).
/// </summary>
public sealed record CommandProgress(CommandStatus Status, string? RequestId, string? PollUrl, JsonElement Response)
{
    public static CommandProgress Parse(JsonElement response)
    {
        var source = response.TryGetProperty("commandResponse", out var legacy) ? legacy : response;

        string? Read(string name) =>
            source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var status = Read("status")?.ToLowerInvariant() switch
        {
            "success" or "completed" or "complete" => CommandStatus.Success,
            "failure" or "failed" or "error" => CommandStatus.Failure,
            null => Read("url") is null ? CommandStatus.Success : CommandStatus.InProgress,
            _ => CommandStatus.InProgress,
        };

        return new CommandProgress(status, Read("requestId"), Read("url"), response);
    }
}
