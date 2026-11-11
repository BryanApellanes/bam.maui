namespace Bam.Maui.Passkeys;

/// <summary>
/// The sign-up form an app collects before creating an account with a passkey.
/// </summary>
public sealed record PasskeyRegistrationRequest
{
    /// <summary>The account identifier the user chose.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The name the authenticator shows for the account.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The user's email address, when the backend asks for one.</summary>
    public string? Email { get; init; }

    /// <summary>An invite code, when the backend's sign-up is invite-only.</summary>
    public string? InviteCode { get; init; }
}

/// <summary>
/// What the backend returns when a registration begins.
/// </summary>
public sealed record PasskeyRegistrationChallenge
{
    /// <summary>The creation options to hand to the authenticator.</summary>
    public required WebAuthnCreationOptions Options { get; init; }

    /// <summary>The handle as the backend normalized it, or null when it returned none; completion uses this spelling.</summary>
    public string? Handle { get; init; }
}

/// <summary>
/// What the backend returns when a sign-in begins.
/// </summary>
public sealed record PasskeyAuthenticationChallenge
{
    /// <summary>The request options to hand to the authenticator.</summary>
    public required WebAuthnRequestOptions Options { get; init; }

    /// <summary>An opaque value the backend uses to find its pending challenge at completion, when it issues one.</summary>
    public string? CeremonyId { get; init; }
}

/// <summary>
/// Who the backend says is signed in after a verified ceremony.  The app takes identity from
/// here, never from what the user typed.
/// </summary>
public sealed record PasskeySignIn
{
    /// <summary>The signed-in account's identifier.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The account's display name, when the backend returned one.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Where the backend suggests the app go next, when it returned a suggestion.</summary>
    public string? NextUrl { get; init; }
}

/// <summary>
/// One passkey registered to the signed-in account.  Every field except the id is optional:
/// what the backend did not report stays null, and the UI shows nothing in its place.
/// </summary>
public sealed record PasskeyCredential
{
    /// <summary>The credential id, as the backend identifies it.</summary>
    public string CredentialId { get; init; } = string.Empty;

    /// <summary>The name given to the passkey, if any.</summary>
    public string? Name { get; init; }

    /// <summary>When the passkey was registered, if the backend reported it.</summary>
    public DateTime? CreatedUtc { get; init; }

    /// <summary>When the passkey last signed in, if the backend reported it.</summary>
    public DateTime? LastUsedUtc { get; init; }

    /// <summary>A description of the device or provider, if the backend reported one.</summary>
    public string? Device { get; init; }

    /// <summary>Whether the passkey is synced to a credential provider, if the backend reported it.</summary>
    public bool? BackedUp { get; init; }
}

/// <summary>
/// A snapshot for deciding what to show a user.
/// </summary>
public sealed record PasskeyStatus
{
    /// <summary>What the device can do.</summary>
    public required PasskeyCapabilities Capabilities { get; init; }

    /// <summary>How many passkeys the signed-in account has, or null when the backend could not be asked (management disabled, not signed in, offline).</summary>
    public int? RegisteredPasskeyCount { get; init; }

    /// <summary>Whether the account is known to have at least one passkey.</summary>
    public bool HasRegisteredPasskeys => RegisteredPasskeyCount is > 0;
}
