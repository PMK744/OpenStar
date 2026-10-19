namespace OpenStar.Core.BrowserLogin;

public enum BrowserLoginStatus
{
    /// <summary>Sign-in finished and the account is linked.</summary>
    Linked,

    /// <summary>GM is asking for a verification code; call back with it.</summary>
    MfaRequired,

    /// <summary>Sign-in could not continue.</summary>
    Failed,
}

/// <summary>
/// Outcome of a browser sign-in step. <see cref="State"/> ties a follow-up code
/// submission to the same live browser session.
/// </summary>
public sealed record BrowserLoginResult(
    BrowserLoginStatus Status,
    string? State = null,
    string? MfaType = null,
    string? Message = null);
