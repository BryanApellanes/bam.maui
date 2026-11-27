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
    /// <param name="name">A name for the new passkey, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new passkey's credential id, as the server identifies it.</returns>
    Task<string> CompleteAdditionalRegistrationAsync(WebAuthnRegistrationResponse response, string? name = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Lists the signed-in account's passkeys with the state of its key set (a waiting lost-device recovery, and whether
    /// one can start).  The default lists the passkeys and reports neither, for a backend that can't recover key sets.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The passkeys and the key set's state.</returns>
    async Task<PasskeyList> GetPasskeyListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PasskeyCredential> passkeys = await GetPasskeysAsync(cancellationToken).ConfigureAwait(false);
        return new PasskeyList { Passkeys = passkeys };
    }

    /// <summary>Removes one of the signed-in account's passkeys on the server.  The credential may remain in the device's credential manager; only the server stops accepting it.</summary>
    /// <param name="credentialId">The credential id, as <see cref="PasskeyCredential.CredentialId"/> reported it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether removing it ended this session (it does when the session was opened with it).</returns>
    Task<PasskeySignOut> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        return DeletePasskeyAsync(credentialId, PasskeyKeySetRemoval.Default, cancellationToken);
    }

    /// <summary>
    /// Removes one of the signed-in account's passkeys on the server, saying what happens to the key set when the
    /// passkey holds it.  A backend that moves key sets refuses <see cref="PasskeyKeySetRemoval.Default"/> for that
    /// passkey with a <see cref="PasskeyException"/> carrying its handover code (see <see cref="BeginKeySetHandoverAsync"/>).
    /// </summary>
    /// <param name="credentialId">The credential id, as <see cref="PasskeyCredential.CredentialId"/> reported it.</param>
    /// <param name="keySet">What happens to the key set.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether removing it ended this session (it does when the session was opened with it).</returns>
    Task<PasskeySignOut> DeletePasskeyAsync(string credentialId, PasskeyKeySetRemoval keySet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Key-set handover: asks the server to move the key set off the passkey that holds it, which is then removed.
    /// The server answers with assertion options for that passkey only.
    /// </summary>
    /// <param name="credentialId">The passkey that holds the key set and is being removed.</param>
    /// <param name="heirCredentialId">The passkey to move the key set to, or null for the one this session signed in with.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The challenge to hand to the authenticator.</returns>
    Task<PasskeyHandoverChallenge> BeginKeySetHandoverAsync(string credentialId, string? heirCredentialId, CancellationToken cancellationToken = default);

    /// <summary>Key-set handover: submits the removed passkey's assertion; the server moves the key set and removes the passkey.</summary>
    /// <param name="credentialId">The passkey being removed, as it was passed to <see cref="BeginKeySetHandoverAsync"/>.</param>
    /// <param name="challenge">The challenge the assertion answers, exactly as <see cref="BeginKeySetHandoverAsync"/> returned it.</param>
    /// <param name="response">The authenticator's assertion response.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether removing it ended this session (it does when the session was opened with it).</returns>
    Task<PasskeySignOut> CompleteKeySetHandoverAsync(string credentialId, PasskeyHandoverChallenge challenge, WebAuthnAssertionResponse response, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lost-device recovery: asks the server to move the key set to <paramref name="heirCredentialId"/> after its waiting
    /// period, taking <paramref name="removedCredentialId"/> (the lost passkey that holds it) out of sign-in.  The server
    /// answers with assertion options for the heir only.
    /// </summary>
    /// <param name="removedCredentialId">The lost passkey that holds the key set, or null when no passkey holds it any more.</param>
    /// <param name="heirCredentialId">The passkey to move the key set to, or null for the one this session signed in with.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The challenge to hand to the authenticator.</returns>
    Task<PasskeyRecoveryChallenge> BeginKeySetRecoveryAsync(string? removedCredentialId, string? heirCredentialId, CancellationToken cancellationToken = default);

    /// <summary>Lost-device recovery: submits the heir's assertion; the server removes the lost passkey now and schedules the move.</summary>
    /// <param name="challenge">The challenge the assertion answers, exactly as <see cref="BeginKeySetRecoveryAsync"/> returned it.</param>
    /// <param name="response">The authenticator's assertion response.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether that ended this session, and when the key set moves.</returns>
    Task<PasskeyRecoveryStarted> CompleteKeySetRecoveryAsync(PasskeyRecoveryChallenge challenge, WebAuthnAssertionResponse response, CancellationToken cancellationToken = default);

    /// <summary>Cancels a waiting lost-device recovery.  Any of the person's sessions can.</summary>
    /// <param name="recoveryId">The recovery's id, as <see cref="PasskeyPendingRecovery.RecoveryId"/> reported it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the server has cancelled it.</returns>
    Task CancelKeySetRecoveryAsync(string recoveryId, CancellationToken cancellationToken = default);

    /// <summary>Renames one of the signed-in account's passkeys.</summary>
    /// <param name="credentialId">The credential id, as <see cref="PasskeyCredential.CredentialId"/> reported it.</param>
    /// <param name="name">The new name, or null (or blank) to clear it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the server has renamed it.</returns>
    Task RenamePasskeyAsync(string credentialId, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session.  An implementation forgets the session it holds (a cookie, a token) however the call ends,
    /// including when it throws, as <see cref="HttpPasskeyBackend"/> does with its cookie jar.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether a session ended, and where the server suggests going.</returns>
    Task<PasskeySignOut> SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the signed-in account's profile, for a sign-up whose profile wasn't written
    /// (<see cref="PasskeySignIn.ProfileCreated"/> false).  Succeeds too when the profile already exists.
    /// </summary>
    /// <param name="profile">The name and email.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the profile is saved.</returns>
    Task SaveProfileAsync(PasskeyProfile profile, CancellationToken cancellationToken = default);
}
