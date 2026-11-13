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
    /// Removes the passkey without moving the key set, which stays on record with no passkey holding it.  Always
    /// possible, for a passkey whose device is lost; a backend ignores it where it changes nothing.
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
