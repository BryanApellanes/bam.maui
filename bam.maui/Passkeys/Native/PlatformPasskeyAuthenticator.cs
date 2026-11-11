namespace Bam.Maui.Passkeys;

/// <summary>
/// The base of the library's own platform authenticators.  It does the part every platform must
/// do the same way: read the relying party's options, refuse them when they are malformed or name
/// a relying party other than the one this app is configured for, and only then hand the parsed
/// request to the platform.  A derived type never sees options that failed that check.
/// </summary>
public abstract class PlatformPasskeyAuthenticator : IPasskeyAuthenticator
{
    /// <summary>Creates the authenticator for an app's configuration.</summary>
    /// <param name="configuration">The app's passkey configuration; its <see cref="PasskeyConfiguration.RelyingPartyId"/> is the only relying party this authenticator serves.</param>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    protected PlatformPasskeyAuthenticator(PasskeyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        RelyingPartyId = configuration.RelyingPartyId;
    }

    /// <summary>The relying party id every ceremony is checked against.</summary>
    protected string RelyingPartyId { get; }

    /// <inheritdoc />
    public abstract Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<WebAuthnRegistrationResponse> CreateAsync(WebAuthnCreationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        WebAuthnCreationRequest request = WebAuthnCreationRequest.Parse(options, RelyingPartyId);
        cancellationToken.ThrowIfCancellationRequested();
        return await CreateCoreAsync(request, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WebAuthnAssertionResponse> AssertAsync(WebAuthnRequestOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        WebAuthnAssertionRequest request = WebAuthnAssertionRequest.Parse(options, RelyingPartyId);
        cancellationToken.ThrowIfCancellationRequested();
        return await AssertCoreAsync(request, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the platform's creation ceremony for options that passed the checks.</summary>
    /// <param name="request">The parsed options.</param>
    /// <param name="options">The same options as the relying party issued them, for a platform that takes the JSON whole.</param>
    /// <param name="cancellationToken">Cancels the ceremony where the platform allows it.</param>
    /// <returns>The registration response.</returns>
    protected abstract Task<WebAuthnRegistrationResponse> CreateCoreAsync(WebAuthnCreationRequest request, WebAuthnCreationOptions options, CancellationToken cancellationToken);

    /// <summary>Runs the platform's sign-in ceremony for options that passed the checks.</summary>
    /// <param name="request">The parsed options.</param>
    /// <param name="options">The same options as the relying party issued them, for a platform that takes the JSON whole.</param>
    /// <param name="cancellationToken">Cancels the ceremony where the platform allows it.</param>
    /// <returns>The assertion response.</returns>
    protected abstract Task<WebAuthnAssertionResponse> AssertCoreAsync(WebAuthnAssertionRequest request, WebAuthnRequestOptions options, CancellationToken cancellationToken);
}
