using System.Text;
using System.Text.Json;

namespace OpenStar.Core.Authentication;

/// <summary>
/// Reads JWT payloads without validating them. GM and B2C tokens are only
/// inspected for metadata (email, VINs); their signatures are checked upstream.
/// </summary>
internal static class Jwt
{
    public static JsonElement? ReadPayload(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: >= 2 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    public static string? ReadString(JsonElement? payload, string name)
    {
        return payload is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
