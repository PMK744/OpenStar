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

    /// <summary>
    /// Optional transparent reverse proxy to GM's login host on this port. It is
    /// off by default (0): GM's Akamai bot protection blocks the proxied
    /// credential POST, so <see cref="BrowserLoginHeadless">browser sign-in</see>
    /// is used instead. Kept for networks or regions where the proxy still works.
    /// </summary>
    public int LoginRelayPort { get; set; }

    /// <summary>
    /// External base URL the browser uses to reach the relay, when it differs
    /// from "this host on <see cref="LoginRelayPort"/>" (e.g. behind another
    /// proxy). Normally left empty.
    /// </summary>
    public string? LoginRelayUrl { get; set; }

    /// <summary>
    /// Run the sign-in Chromium headless. Set false to watch it (needs a display,
    /// or Xvfb) when debugging why a sign-in fails.
    /// </summary>
    public bool BrowserLoginHeadless { get; set; } = true;

    /// <summary>How long a single browser sign-in step may take before giving up.</summary>
    public int BrowserLoginTimeoutSeconds { get; set; } = 120;

    public int CommandPollingIntervalSeconds { get; set; } = 6;

    public int CommandPollingTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// How often the background service refreshes the Microsoft token so the
    /// refresh token's sliding window never lapses.
    /// </summary>
    public int TokenKeepAliveHours { get; set; } = 12;
}
