namespace Bam.Maui.Passkeys;

/// <summary>What happens to the account's key set when the passkey that holds it (<see cref="PasskeyCredential.IsRegisteredKeySet"/>) is removed.</summary>
public enum PasskeyKeySetRemoval
{
    /// <summary>
    /// The backend decides.  A backend that can move the key set refuses to remove its passkey and asks for a handover
    /// instead (for the bam account endpoints, the <c>KeySetHandoverRequired</c> code); one that can't removes it and
    /// keeps the key set on record.  Removing any other passkey is unaffected.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Removes the passkey without moving the key set.  Sign-in with it stops at once, but its key still counts as the
    /// account key until the key set is replaced (<see cref="IPasskeyClient.RecoverKeySetAsync"/>).  Always possible, for
    /// a passkey whose device is lost; a backend ignores it where it changes nothing.
    /// </summary>
    KeepKeySet
}

/// <summary>
/// What the backend returns when a key-set handover begins: assertion options that allow only the passkey being
/// removed, so its holder proves they have it before the key set moves to another passkey.
/// </summary>
public sealed record PasskeyHandoverChallenge
{
    /// <summary>The request options to hand to the authenticator.</summary>
    public required WebAuthnRequestOptions Options { get; init; }

    /// <summary>An opaque value the backend uses to find the pending handover at completion; it works once.</summary>
    public required string HandoverId { get; init; }

    /// <summary>Names the type only, so the handover id never lands in a log through string formatting.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(PasskeyHandoverChallenge);
}

/// <summary>
/// What the backend returns when a lost-device recovery begins: assertion options that allow only the heir (the
/// passkey that will take over the key set), with user verification required, so the person proves they hold it.
/// </summary>
public sealed record PasskeyRecoveryChallenge
{
    /// <summary>The request options to hand to the authenticator.</summary>
    public required WebAuthnRequestOptions Options { get; init; }

    /// <summary>An opaque value the backend uses to find the recovery at completion; it is also the waiting recovery's id.</summary>
    public required string RecoveryId { get; init; }

    /// <summary>Names the type only, so the recovery id never lands in a log through string formatting.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(PasskeyRecoveryChallenge);
}

/// <summary>What starting a lost-device recovery did: the lost passkey is out of sign-in now, and the key set moves later.</summary>
public sealed record PasskeyRecoveryStarted
{
    /// <summary>Whether the session ended: it does when it was opened with the passkey taken out of sign-in.</summary>
    public required PasskeySignOut SignOut { get; init; }

    /// <summary>When the key set moves to the heir, unless the recovery is cancelled first.</summary>
    public DateTime EffectiveUtc { get; init; }
}

/// <summary>
/// A lost-device recovery that is waiting: the key set moves to <see cref="HeirCredentialId"/> at
/// <see cref="EffectiveUtc"/> unless one of the person's sessions cancels it first.
/// </summary>
public sealed record PasskeyPendingRecovery
{
    /// <summary>The recovery's id, for <see cref="IPasskeyClient.CancelKeySetRecoveryAsync"/>.</summary>
    public string RecoveryId { get; init; } = string.Empty;

    /// <summary>The passkey the recovery took out of sign-in, or null when none was named.</summary>
    public string? RemovedCredentialId { get; init; }

    /// <summary>The passkey the key set moves to.</summary>
    public string HeirCredentialId { get; init; } = string.Empty;

    /// <summary>When the key set moves.</summary>
    public DateTime EffectiveUtc { get; init; }

    /// <summary>Names the type only: the recovery id cancels the recovery and does not belong in a log.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(PasskeyPendingRecovery);
}
