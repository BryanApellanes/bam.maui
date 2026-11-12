namespace Bam.Maui.Passkeys;

/// <summary>
/// What an app calls to register, sign in with and manage passkeys.  Each ceremony runs the same
/// way: the <see cref="IPasskeyBackend"/> issues a challenge, the <see cref="IPasskeyAuthenticator"/>
/// runs the operating system's prompt, the backend verifies the result.
/// <para>
/// No ceremony or management method throws for an expected failure; the three probes
/// (<see cref="IsSupportedAsync"/>, <see cref="GetCapabilitiesAsync"/>, <see cref="GetPasskeyStatusAsync"/>)
/// throw only <see cref="OperationCanceledException"/>, and only when the caller cancels.  Unsupported devices, dismissed prompts, network
/// faults and server refusals all come back as a failed <see cref="PasskeyResult"/> whose
/// <see cref="PasskeyError"/> carries a category and a message that is safe to show.
/// </para>
/// <para>
/// The library's screens show <see cref="PasskeyError.Message"/> to the user as it is.  An implementation other than
/// <see cref="PasskeyClient"/> must take that message from <see cref="PasskeyMessages"/> (or other text the app wrote),
/// never from a server response, an exception or a WebAuthn payload.
/// </para>
/// </summary>
public interface IPasskeyClient
{
    /// <summary>Whether a passkey ceremony can run on this device right now.  Never prompts.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>True only when the platform authenticator reports <see cref="PasskeyAvailability.Available"/>.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled; a cancelled probe says nothing about the device.</exception>
    Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default);

    /// <summary>What this device can do, and why not when it cannot.  Never prompts.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The capabilities; a probe that fails reports not supported.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled; a cancelled probe says nothing about the device.</exception>
    Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an account with its first passkey and signs it in.</summary>
    /// <param name="request">The sign-up form.</param>
    /// <param name="cancellationToken">Cancels the ceremony.</param>
    /// <returns>The signed-in account on success.</returns>
    Task<PasskeyResult<PasskeySignIn>> RegisterPasskeyAsync(PasskeyRegistrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adds another passkey to the signed-in account.</summary>
    /// <param name="name">A name for the new passkey, or null.</param>
    /// <param name="cancellationToken">Cancels the ceremony.</param>
    /// <returns>The new passkey's credential id, once the backend has stored it.</returns>
    Task<PasskeyResult<string>> AddPasskeyAsync(string? name = null, CancellationToken cancellationToken = default);

    /// <summary>Signs in without a username: the operating system offers the passkeys it holds for the relying party.</summary>
    /// <param name="cancellationToken">Cancels the ceremony.</param>
    /// <returns>The signed-in account on success.</returns>
    Task<PasskeyResult<PasskeySignIn>> AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>Signs in a named account with one of its passkeys.</summary>
    /// <param name="handle">The account identifier the user entered.</param>
    /// <param name="cancellationToken">Cancels the ceremony.</param>
    /// <returns>The signed-in account on success.</returns>
    Task<PasskeyResult<PasskeySignIn>> AuthenticateWithUsernameAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>Lists the signed-in account's passkeys.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The registered passkeys on success.</returns>
    Task<PasskeyResult<IReadOnlyList<PasskeyCredential>>> GetRegisteredPasskeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes one of the signed-in account's passkeys on the backend.</summary>
    /// <param name="credentialId">The credential to revoke.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Once the backend has removed the credential: whether that ended this session (it does when the session was opened with it).</returns>
    Task<PasskeyResult<PasskeySignOut>> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default);

    /// <summary>Renames one of the signed-in account's passkeys.</summary>
    /// <param name="credentialId">The credential to rename.</param>
    /// <param name="name">The new name, or null (or blank) to clear it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success once the backend has renamed it.</returns>
    Task<PasskeyResult> RenamePasskeyAsync(string credentialId, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session.  Not gated on a feature: a signed-in person can always leave.  The session ends on this device
    /// however the call ends: a failure says the server may not have heard (its session then lasts until it expires), not
    /// that the person is still signed in here.  Callers treat a failed sign-out as signed out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether a session ended, and where the backend suggests going.</returns>
    Task<PasskeyResult<PasskeySignOut>> SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the signed-in account's profile, for a sign-up whose profile wasn't written
    /// (<see cref="PasskeySignIn.ProfileCreated"/> false).
    /// </summary>
    /// <param name="profile">The name and email.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success once the backend has the profile.</returns>
    Task<PasskeyResult> SaveProfileAsync(PasskeyProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// A summary for deciding what to show: device capabilities, and how many passkeys the
    /// signed-in account has when the backend can be asked.  Never prompts; what could not be
    /// determined is left null.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The status.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    Task<PasskeyStatus> GetPasskeyStatusAsync(CancellationToken cancellationToken = default);
}
