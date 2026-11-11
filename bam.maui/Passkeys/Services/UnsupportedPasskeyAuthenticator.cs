namespace Bam.Maui.Passkeys;

/// <summary>
/// The authenticator bound when no platform implementation is registered (a platform this
/// library has no integration for, or a host that has not added one).  It reports
/// <see cref="PasskeyAvailability.NotSupported"/> and refuses ceremonies, so the library never
/// claims passkey support it cannot deliver and apps fall back to their other sign-in path.
/// </summary>
public sealed class UnsupportedPasskeyAuthenticator : IPasskeyAuthenticator
{
    /// <inheritdoc />
    public Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PasskeyCapabilities.Unsupported());
    }

    /// <inheritdoc />
    /// <exception cref="PasskeyException">Always, with <see cref="PasskeyErrorCategory.UnsupportedOperatingSystem"/>.</exception>
    public Task<WebAuthnRegistrationResponse> CreateAsync(WebAuthnCreationOptions options, CancellationToken cancellationToken = default)
    {
        throw new PasskeyException(PasskeyErrorCategory.UnsupportedOperatingSystem, "No platform passkey authenticator is registered.");
    }

    /// <inheritdoc />
    /// <exception cref="PasskeyException">Always, with <see cref="PasskeyErrorCategory.UnsupportedOperatingSystem"/>.</exception>
    public Task<WebAuthnAssertionResponse> AssertAsync(WebAuthnRequestOptions options, CancellationToken cancellationToken = default)
    {
        throw new PasskeyException(PasskeyErrorCategory.UnsupportedOperatingSystem, "No platform passkey authenticator is registered.");
    }
}
