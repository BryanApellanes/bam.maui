namespace Bam.Maui.Passkeys;

/// <summary>
/// The calls <see cref="WindowsPasskeyAuthenticator"/> makes into <c>webauthn.dll</c>, with
/// managed arguments.  <see cref="WindowsWebAuthnApi"/> is the real one; tests substitute their own.
/// </summary>
internal interface IWindowsWebAuthnApi
{
    /// <summary><c>WebAuthNGetApiVersionNumber</c>.  Throws when the library or the entry point is missing.</summary>
    int GetApiVersionNumber();

    /// <summary><c>WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable</c>; false when the call fails.</summary>
    bool IsUserVerifyingPlatformAuthenticatorAvailable();

    /// <summary><c>WebAuthNAuthenticatorMakeCredential</c>.  Blocks until the prompt closes.</summary>
    WindowsAttestation MakeCredential(IntPtr window, WindowsMakeCredentialCall call, CancellationToken cancellationToken);

    /// <summary><c>WebAuthNAuthenticatorGetAssertion</c>.  Blocks until the prompt closes.</summary>
    WindowsAssertion GetAssertion(IntPtr window, WindowsGetAssertionCall call, CancellationToken cancellationToken);
}

/// <summary>The arguments of one <c>WebAuthNAuthenticatorMakeCredential</c> call.</summary>
internal sealed class WindowsMakeCredentialCall
{
    public required string RelyingPartyId { get; init; }
    public required string RelyingPartyName { get; init; }
    public required byte[] UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserDisplayName { get; init; }
    public required IReadOnlyList<int> Algorithms { get; init; }
    public required byte[] ClientDataJson { get; init; }
    public required int TimeoutMilliseconds { get; init; }
    public required IReadOnlyList<byte[]> ExcludeCredentialIds { get; init; }

    /// <summary>A <c>WEBAUTHN_AUTHENTICATOR_ATTACHMENT_*</c> value.</summary>
    public required int AuthenticatorAttachment { get; init; }
    public required bool RequireResidentKey { get; init; }
    public required bool PreferResidentKey { get; init; }

    /// <summary>A <c>WEBAUTHN_USER_VERIFICATION_REQUIREMENT_*</c> value.</summary>
    public required int UserVerificationRequirement { get; init; }

    /// <summary>A <c>WEBAUTHN_ATTESTATION_CONVEYANCE_PREFERENCE_*</c> value.</summary>
    public required int AttestationConveyancePreference { get; init; }
}

/// <summary>The arguments of one <c>WebAuthNAuthenticatorGetAssertion</c> call.</summary>
internal sealed class WindowsGetAssertionCall
{
    public required string RelyingPartyId { get; init; }
    public required byte[] ClientDataJson { get; init; }
    public required int TimeoutMilliseconds { get; init; }
    public required IReadOnlyList<byte[]> AllowCredentialIds { get; init; }

    /// <summary>A <c>WEBAUTHN_USER_VERIFICATION_REQUIREMENT_*</c> value.</summary>
    public required int UserVerificationRequirement { get; init; }
}

/// <summary>What <c>WebAuthNAuthenticatorMakeCredential</c> returned.</summary>
internal sealed class WindowsAttestation
{
    /// <summary>The call's <c>HRESULT</c>; the other members are set only on success.</summary>
    public required int Result { get; init; }
    public byte[]? AttestationObject { get; init; }
    public byte[]? CredentialId { get; init; }

    /// <summary>The <c>WEBAUTHN_CTAP_TRANSPORT_*</c> bit of the transport used; 0 when the library did not say.</summary>
    public int UsedTransport { get; init; }
}

/// <summary>What <c>WebAuthNAuthenticatorGetAssertion</c> returned.</summary>
internal sealed class WindowsAssertion
{
    /// <summary>The call's <c>HRESULT</c>; the other members are set only on success.</summary>
    public required int Result { get; init; }
    public byte[]? AuthenticatorData { get; init; }
    public byte[]? Signature { get; init; }
    public byte[]? CredentialId { get; init; }
    public byte[]? UserId { get; init; }
}
