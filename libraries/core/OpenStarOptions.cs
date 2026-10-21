namespace OpenStar.Core;

public sealed class OpenStarOptions
{
    public const string SectionName = "OpenStar";

    /// <summary>
    /// Directory holding the encrypted account state and data protection keys.
    /// </summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>
    /// Optional admin key. When set it always authorizes admin requests, which
    /// is useful for recovering access if the generated admin key is lost.
    /// </summary>
    public string? AdminKey { get; set; }

    /// <summary>
    /// GM mobile API host that serves garage, command, and telemetry routes.
    /// </summary>
    public string ServiceUrl { get; set; } = "https://na-mobile-api.gm.com";

    public int CommandPollingIntervalSeconds { get; set; } = 6;

    public int CommandPollingTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// How often the background service refreshes the Microsoft token so the
    /// refresh token's sliding window never lapses.
    /// </summary>
    public int TokenKeepAliveHours { get; set; } = 12;
}
