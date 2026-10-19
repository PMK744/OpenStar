namespace OpenStar.Core.Authentication;

/// <summary>
/// Raised when the linked account cannot produce a usable GM token and the
/// user has to sign in again through the setup UI.
/// </summary>
public sealed class OpenStarAuthException(string message, Exception? innerException = null)
    : Exception(message, innerException);
