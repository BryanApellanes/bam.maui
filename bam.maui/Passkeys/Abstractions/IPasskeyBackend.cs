namespace Bam.Maui.Passkeys;

/// <summary>
/// The relying-party seam: the server that issues challenges, verifies every ceremony and owns
/// the credential store and the session.  The app never verifies a signature or a challenge
/// itself; a ceremony has succeeded only when the matching <c>Complete</c> call returns.
/// <para>
/// Implementations throw <see cref="PasskeyException"/> with the category that fits a refusal
/// (<see cref="PasskeyErrorCategory.ExpiredChallenge"/>, <see cref="PasskeyErrorCategory.ServerVerificationFailed"/>,
/// <see cref="PasskeyErrorCategory.InvalidServerResponse"/>); transport exceptions may propagate
/// and are normalized by <see cref="PasskeyErrorNormalizer"/>.  Session state (a cookie, a token)
/// is the implementation's to hold.
/// </para>
/// </summary>
public interface IPasskeyBackend
{
    /// <summary>Sign-up: asks the server for creation options for a new account.</summary>
    /// <param name="request">The sign-up form.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The challenge to hand to the authenticator.</returns>
    Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(PasskeyRegistrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sign-up: submits the authenticator's response for verification.  The sign-up form is sent
    /// again because the server holds nothing between the two calls except the challenge.
    /// </summary>
    /// <param name="request">The sign-up form, with the handle as the server normalized it.</param>
    /// <param name="response">The authenticator's registration response.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The account the server created and signed in.</returns>
    Task<PasskeySignIn> CompleteRegistrationAsync(PasskeyRegistrationRequest request, WebAuthnRegistrationResponse response, CancellationToken cancellationToken = default);

    /// <summary>Adds a passkey to the signed-in account: asks the server for creation options.  The server takes the account from the session, never from the app.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The creation options to hand to the authenticator.</returns>
    Task<WebAuthnCreationOptions> BeginAdditionalRegistrationAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a passkey to the signed-in account: submits the authenticator's response for verification.</summary>
    /// <param name="response">The authenticator's registration response.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task CompleteAdditionalRegistrationAsync(WebAuthnRegistrationResponse response, CancellationToken cancellationToken = default);

    /// <summary>Sign-in: asks the server for request options.</summary>
    /// <param name="handle">The account to sign in, or null for username-less (discoverable) sign-in.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The challenge to hand to the authenticator.</returns>
    Task<PasskeyAuthenticationChallenge> BeginAuthenticationAsync(string? handle, CancellationToken cancellationToken = default);

    /// <summary>Sign-in: submits the assertion for verification and establishes the session.</summary>
    /// <param name="challenge">The challenge the assertion answers, exactly as <see cref="BeginAuthenticationAsync"/> returned it.</param>
    /// <param name="response">The authenticator's assertion response.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Who the server says signed in.</returns>
    Task<PasskeySignIn> CompleteAuthenticationAsync(PasskeyAuthenticationChallenge challenge, WebAuthnAssertionResponse response, CancellationToken cancellationToken = default);

    /// <summary>Lists the signed-in account's passkeys.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The registered passkeys, with only the metadata the server actually has.</returns>
    Task<IReadOnlyList<PasskeyCredential>> GetPasskeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes one of the signed-in account's passkeys on the server.  The credential may remain in the device's credential manager; only the server stops accepting it.</summary>
    /// <param name="credentialId">The credential id, as <see cref="PasskeyCredential.CredentialId"/> reported it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default);
}
