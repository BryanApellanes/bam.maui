using Bam.Logging;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Runs passkey ceremonies by passing WebAuthn JSON between the <see cref="IPasskeyBackend"/>
/// and the <see cref="IPasskeyAuthenticator"/>.  It decides nothing about security: the operating
/// system owns the prompt and the key, the backend owns verification, and an operation succeeds
/// only when the backend's <c>Complete</c> call returns.
/// <para>
/// Every exception from either collaborator is caught and returned as a failed
/// <see cref="PasskeyResult"/>.  Failures are logged by operation, category and exception type
/// only; exception messages and WebAuthn payloads are never logged.
/// </para>
/// </summary>
public sealed class PasskeyClient : IPasskeyClient
{
    private readonly IPasskeyBackend _backend;
    private readonly IPasskeyAuthenticator _authenticator;
    private readonly PasskeyConfiguration _configuration;
    private readonly PasskeyErrorNormalizer _normalizer;
    private readonly ILogger? _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="backend">The relying-party server.</param>
    /// <param name="authenticator">The platform authenticator.</param>
    /// <param name="configuration">The app's passkey configuration; its feature switches gate each operation.</param>
    /// <param name="normalizer">Turns exceptions into user-safe errors.</param>
    /// <param name="logger">Where failures are recorded, or null to record nothing.</param>
    public PasskeyClient(IPasskeyBackend backend, IPasskeyAuthenticator authenticator, PasskeyConfiguration configuration, PasskeyErrorNormalizer normalizer, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(normalizer);
        _backend = backend;
        _authenticator = authenticator;
        _configuration = configuration;
        _normalizer = normalizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        PasskeyCapabilities capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return capabilities.IsSupported;
    }

    /// <inheritdoc />
    public async Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PasskeyCapabilities? capabilities = await _authenticator.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            return capabilities ?? PasskeyCapabilities.Unsupported();
        }
        catch (Exception exception)
        {
            // A probe that fails is reported as not supported; claiming support here would send the user into a ceremony that cannot run.
            LogFailure("capability probe", PasskeyErrorNormalizer.Categorize(exception), exception);
            return PasskeyCapabilities.Unsupported();
        }
    }

    /// <inheritdoc />
    public Task<PasskeyResult<PasskeySignIn>> RegisterPasskeyAsync(PasskeyRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        return RunAsync<PasskeySignIn>("registration", _configuration.Features.EnableRegistration, true, async token =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Handle))
            {
                throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, "A registration request with a handle is required.");
            }
            PasskeyRegistrationChallenge challenge = await _backend.BeginRegistrationAsync(request, token).ConfigureAwait(false);
            if (challenge?.Options is null)
            {
                throw new PasskeyException(PasskeyErrorCategory.InvalidServerResponse, "The backend returned no creation options.");
            }
            WebAuthnRegistrationResponse response = Require(await _authenticator.CreateAsync(challenge.Options, token).ConfigureAwait(false));
            PasskeyRegistrationRequest completion = string.IsNullOrWhiteSpace(challenge.Handle) ? request : request with { Handle = challenge.Handle };
            PasskeySignIn signIn = await _backend.CompleteRegistrationAsync(completion, response, token).ConfigureAwait(false);
            return RequireSignIn(signIn);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasskeyResult> AddPasskeyAsync(CancellationToken cancellationToken = default)
    {
        PasskeyResult<bool> result = await RunAsync<bool>("additional registration", _configuration.Features.EnableManagement, true, async token =>
        {
            WebAuthnCreationOptions options = await _backend.BeginAdditionalRegistrationAsync(token).ConfigureAwait(false);
            if (options is null)
            {
                throw new PasskeyException(PasskeyErrorCategory.InvalidServerResponse, "The backend returned no creation options.");
            }
            WebAuthnRegistrationResponse response = Require(await _authenticator.CreateAsync(options, token).ConfigureAwait(false));
            await _backend.CompleteAdditionalRegistrationAsync(response, token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return WithoutValue(result);
    }

    /// <inheritdoc />
    public Task<PasskeyResult<PasskeySignIn>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        bool enabled = _configuration.Features.EnableAuthentication && _configuration.Features.EnableUsernameLessAuthentication;
        return RunAsync<PasskeySignIn>("username-less authentication", enabled, true, token => SignInAsync(null, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<PasskeyResult<PasskeySignIn>> AuthenticateWithUsernameAsync(string handle, CancellationToken cancellationToken = default)
    {
        return RunAsync<PasskeySignIn>("authentication", _configuration.Features.EnableAuthentication, true, token =>
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, "A handle is required for username-first sign-in.");
            }
            return SignInAsync(handle, token);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PasskeyResult<IReadOnlyList<PasskeyCredential>>> GetRegisteredPasskeysAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync<IReadOnlyList<PasskeyCredential>>("passkey listing", _configuration.Features.EnableManagement, false, async token =>
        {
            IReadOnlyList<PasskeyCredential> credentials = await _backend.GetPasskeysAsync(token).ConfigureAwait(false);
            return credentials ?? throw new PasskeyException(PasskeyErrorCategory.InvalidServerResponse, "The backend returned no credential list.");
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasskeyResult> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        PasskeyResult<bool> result = await RunAsync<bool>("passkey deletion", _configuration.Features.EnableManagement, false, async token =>
        {
            if (string.IsNullOrWhiteSpace(credentialId))
            {
                throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, "A credential id is required.");
            }
            await _backend.DeletePasskeyAsync(credentialId, token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return WithoutValue(result);
    }

    /// <inheritdoc />
    public async Task<PasskeyStatus> GetPasskeyStatusAsync(CancellationToken cancellationToken = default)
    {
        PasskeyCapabilities capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        int? count = null;
        if (_configuration.Features.EnableManagement)
        {
            PasskeyResult<IReadOnlyList<PasskeyCredential>> listing = await GetRegisteredPasskeysAsync(cancellationToken).ConfigureAwait(false);
            if (listing.Succeeded && listing.Value is not null)
            {
                count = listing.Value.Count;
            }
        }
        return new PasskeyStatus { Capabilities = capabilities, RegisteredPasskeyCount = count };
    }

    private async Task<PasskeySignIn> SignInAsync(string? handle, CancellationToken cancellationToken)
    {
        PasskeyAuthenticationChallenge challenge = await _backend.BeginAuthenticationAsync(handle, cancellationToken).ConfigureAwait(false);
        if (challenge?.Options is null)
        {
            throw new PasskeyException(PasskeyErrorCategory.InvalidServerResponse, "The backend returned no request options.");
        }
        WebAuthnAssertionResponse? response = await _authenticator.AssertAsync(challenge.Options, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The authenticator returned no assertion.");
        }
        PasskeySignIn signIn = await _backend.CompleteAuthenticationAsync(challenge, response, cancellationToken).ConfigureAwait(false);
        return RequireSignIn(signIn);
    }

    private async Task<PasskeyResult<TValue>> RunAsync<TValue>(string operation, bool enabled, bool needsAuthenticator, Func<CancellationToken, Task<TValue>> work, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return PasskeyResult<TValue>.Failure(_normalizer.FromCategory(PasskeyErrorCategory.FeatureDisabled));
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (needsAuthenticator)
            {
                PasskeyCapabilities capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
                if (!capabilities.IsSupported)
                {
                    return PasskeyResult<TValue>.Failure(_normalizer.FromCategory(capabilities.ToErrorCategory()));
                }
            }
            TValue value = await work(cancellationToken).ConfigureAwait(false);
            return PasskeyResult<TValue>.Success(value);
        }
        catch (Exception exception)
        {
            PasskeyError error = _normalizer.Normalize(exception);
            LogFailure(operation, error.Category, exception);
            return PasskeyResult<TValue>.Failure(error);
        }
    }

    private static WebAuthnRegistrationResponse Require(WebAuthnRegistrationResponse? response)
    {
        return response ?? throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The authenticator returned no credential.");
    }

    private static PasskeySignIn RequireSignIn(PasskeySignIn? signIn)
    {
        if (signIn is null || string.IsNullOrWhiteSpace(signIn.Handle))
        {
            // Success without an identity is not a sign-in; the app must never fall back to what the user typed.
            throw new PasskeyException(PasskeyErrorCategory.InvalidServerResponse, "The backend did not name the signed-in account.");
        }
        return signIn;
    }

    private static PasskeyResult WithoutValue(PasskeyResult<bool> result)
    {
        return result.Error is null ? PasskeyResult.Success() : PasskeyResult.Failure(result.Error);
    }

    private void LogFailure(string operation, PasskeyErrorCategory category, Exception exception)
    {
        if (_logger is null)
        {
            return;
        }
        // Type name only: exception messages can carry server text or payload fragments.
        if (category == PasskeyErrorCategory.UserCancelled)
        {
            _logger.Info("Passkey {0} cancelled by the user.", operation);
        }
        else
        {
            _logger.Warning("Passkey {0} failed: category {1}, exception {2}.", operation, category.ToString(), exception.GetType().Name);
        }
    }
}
