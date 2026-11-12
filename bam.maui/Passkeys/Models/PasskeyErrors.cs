namespace Bam.Maui.Passkeys;

/// <summary>
/// The application-level reasons a passkey operation can fail.  Platform and transport exceptions
/// are folded into these so an app handles one vocabulary on every platform.
/// </summary>
public enum PasskeyErrorCategory
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The user dismissed the operating system prompt.  Not a security failure.</summary>
    UserCancelled,

    /// <summary>The device holds no passkey that fits the request.</summary>
    NoCredentials,

    /// <summary>The platform or operating system version cannot run passkey ceremonies.</summary>
    UnsupportedOperatingSystem,

    /// <summary>No credential provider is installed or enabled.</summary>
    MissingCredentialProvider,

    /// <summary>The device has no screen lock, biometric or PIN configured.</summary>
    DeviceSecurityNotConfigured,

    /// <summary>The backend could not be reached.</summary>
    NetworkFailure,

    /// <summary>The backend answered with something that is not a valid response.</summary>
    InvalidServerResponse,

    /// <summary>The WebAuthn options, or what the caller supplied, were not usable.</summary>
    InvalidOptions,

    /// <summary>The challenge expired before the ceremony finished.</summary>
    ExpiredChallenge,

    /// <summary>The backend refused the ceremony.</summary>
    ServerVerificationFailed,

    /// <summary>The platform passkey API failed.</summary>
    PlatformFailure,

    /// <summary>The operation is switched off in <see cref="PasskeyFeatures"/>.</summary>
    FeatureDisabled,

    /// <summary>The device already holds a passkey for this account, so another was not created.</summary>
    CredentialAlreadyRegistered,

    /// <summary>The backend needs a signed-in session and has none (it expired, or the user signed out).</summary>
    NotSignedIn,

    /// <summary>The backend is limiting requests; trying again later may work.</summary>
    RateLimited,

    /// <summary>Anything else.</summary>
    Unknown
}

/// <summary>
/// A normalized failure: a category for the app to branch on and a message that is safe to show.
/// It never carries exception text, stack traces or WebAuthn payloads.
/// </summary>
public sealed record PasskeyError
{
    /// <summary>Creates an error.</summary>
    /// <param name="category">Why the operation failed.</param>
    /// <param name="message">The user-facing message, taken from <see cref="PasskeyMessages"/>.</param>
    /// <param name="backendCode">The backend's own refusal code, when it sent one (see <see cref="BackendCode"/>).</param>
    public PasskeyError(PasskeyErrorCategory category, string message, string? backendCode = null)
    {
        Category = category;
        Message = message;
        BackendCode = backendCode;
    }

    /// <summary>
    /// The backend's coarse refusal code, when the backend sent one (for the bam account endpoints,
    /// one of their <c>AccountErrorCodes</c>, such as <c>HandleTaken</c> or <c>InviteRequired</c>).
    /// It lets a screen tell refusals within a category apart; it is a code, never text to show.
    /// </summary>
    public string? BackendCode { get; }

    /// <summary>Why the operation failed.</summary>
    public PasskeyErrorCategory Category { get; }

    /// <summary>A message that is safe to show the user.</summary>
    public string Message { get; }

    /// <summary>True when the user dismissed the prompt; apps return to the previous state without an error screen.</summary>
    public bool IsCancellation => Category == PasskeyErrorCategory.UserCancelled;
}

/// <summary>
/// Thrown by an <see cref="IPasskeyAuthenticator"/> or <see cref="IPasskeyBackend"/> to report a
/// failure with a known category.  <see cref="IPasskeyClient"/> catches it; apps normally see a
/// <see cref="PasskeyError"/> instead.  The message is for diagnostics and is never shown to a user.
/// </summary>
public sealed class PasskeyException : Exception
{
    /// <summary>Creates an exception for a category.</summary>
    /// <param name="category">Why the operation failed.</param>
    /// <param name="diagnosticMessage">A message for developers; must not contain secrets or WebAuthn payloads.</param>
    /// <param name="innerException">The platform or transport exception, if any.</param>
    /// <param name="backendCode">The backend's coarse refusal code, if it sent one; see <see cref="PasskeyError.BackendCode"/>.</param>
    public PasskeyException(PasskeyErrorCategory category, string? diagnosticMessage = null, Exception? innerException = null, string? backendCode = null)
        : base(diagnosticMessage ?? $"Passkey operation failed: {category}.", innerException)
    {
        Category = category;
        BackendCode = backendCode;
    }

    /// <summary>The backend's coarse refusal code, if it sent one.</summary>
    public string? BackendCode { get; }

    /// <summary>Why the operation failed.</summary>
    public PasskeyErrorCategory Category { get; }
}
