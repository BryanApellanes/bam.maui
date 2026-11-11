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
    /// <param name="cancellationToken">Cancels the ceremony.</param>
    /// <returns>Success once the backend has stored the new credential.</returns>
    Task<PasskeyResult> AddPasskeyAsync(CancellationToken cancellationToken = default);

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
    /// <returns>Success once the backend has removed the credential.</returns>
    Task<PasskeyResult> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default);

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
