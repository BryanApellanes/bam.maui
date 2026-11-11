namespace Bam.Maui.Passkeys;

/// <summary>
/// The native seam: one implementation per platform runs the operating system's own passkey
/// ceremony (Android Credential Manager, Apple AuthenticationServices, Windows <c>webauthn.dll</c>).
/// The operating system owns the biometric prompt, the credential picker and the key; an
/// implementation only translates between standard WebAuthn JSON and the platform API.
/// <para>
/// Implementations report failures by throwing <see cref="PasskeyException"/> with the category
/// that fits, and report a dismissed prompt as <see cref="PasskeyErrorCategory.UserCancelled"/>
/// (or by letting <see cref="OperationCanceledException"/> propagate).  They never log or surface
/// the WebAuthn payloads.
/// </para>
/// </summary>
public interface IPasskeyAuthenticator
{
    /// <summary>
    /// Reports what this device can do right now.  Must not prompt the user and must not report
    /// <see cref="PasskeyAvailability.Available"/> unless a ceremony can actually run.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The device's passkey capabilities.</returns>
    Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a passkey: shows the operating system's creation prompt for the given options and
    /// returns the authenticator's attestation response.
    /// </summary>
    /// <param name="options">The relying party's creation options, as issued by the backend.</param>
    /// <param name="cancellationToken">Cancels the ceremony where the platform allows it.</param>
    /// <returns>The registration response to send back to the backend unchanged.</returns>
    /// <exception cref="PasskeyException">The ceremony did not produce a credential.</exception>
    Task<WebAuthnRegistrationResponse> CreateAsync(WebAuthnCreationOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs in with a passkey: shows the operating system's credential prompt for the given
    /// options and returns the signed assertion.
    /// </summary>
    /// <param name="options">The relying party's request options, as issued by the backend.</param>
    /// <param name="cancellationToken">Cancels the ceremony where the platform allows it.</param>
    /// <returns>The assertion response to send back to the backend unchanged.</returns>
    /// <exception cref="PasskeyException">The ceremony did not produce an assertion.</exception>
    Task<WebAuthnAssertionResponse> AssertAsync(WebAuthnRequestOptions options, CancellationToken cancellationToken = default);
}
