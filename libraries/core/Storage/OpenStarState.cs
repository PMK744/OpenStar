using OpenStar.Core.Authentication;
using OpenStar.Core.Security;

namespace OpenStar.Core.Storage;

/// <summary>
/// Everything OpenStar persists. The whole document is encrypted at rest.
/// </summary>
public sealed class OpenStarState
{
    /// <summary>
    /// Stable device identifier sent to GM, mirroring a mobile app install.
    /// </summary>
    public string DeviceId { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    public string? Email { get; set; }

    public DateTimeOffset? LinkedAt { get; set; }

    public MicrosoftTokenSet? Microsoft { get; set; }

    public GmTokenSet? Gm { get; set; }

    public string? LastAuthError { get; set; }

    public string? AdminKeyHash { get; set; }

    public List<ApiKeyRecord> ApiKeys { get; set; } = [];
}
