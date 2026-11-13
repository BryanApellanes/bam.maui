using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Bam.Maui.Passkeys;

/// <summary>
/// An <see cref="IPasskeyBackend"/> over the bam account endpoints (<c>bam.useraccounts.web</c>):
/// JSON over https under <see cref="PasskeyHttpBackendOptions.AccountEndpoint"/>, with the session
/// held in a cookie.
/// <para>
/// Every request carries <see cref="RequestHeaderName"/> and no <c>Origin</c>, which is how the
/// endpoints recognize a native client.  WebAuthn options are handed to the authenticator exactly
/// as the server sent them, and the authenticator's response is sent back unchanged.  A refusal
/// becomes a <see cref="PasskeyException"/> whose category comes from the endpoint's
/// <c>error</c> code (carried as <see cref="PasskeyException.BackendCode"/>) or, without one, from
/// the HTTP status.  The server's <c>message</c> is never used and no exception quotes a body.
/// </para>
/// </summary>
public sealed class HttpPasskeyBackend : IPasskeyBackend, IDisposable
{
    /// <summary>The header every state-changing request to the account endpoints must carry.</summary>
    public const string RequestHeaderName = "X-Bam-Account";

    /// <summary>The value sent in <see cref="RequestHeaderName"/>; the endpoints accept any non-empty value.</summary>
    public const string RequestHeaderValue = "bam.maui";

    private const int MaxPasskeys = 1000;
    private const int MaxCeremonyIdLength = 1024;
    private const int MaxCredentialIdLength = 1400;
    private const int MaxBackendCodeLength = 64;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly PasskeySessionCookieJar? _cookieJar;
    private readonly Uri _accountEndpoint;
    private readonly int _maxResponseBytes;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Creates a backend over an <see cref="HttpClient"/> the host built.  The client must keep the
    /// session cookie between requests (a <see cref="CookieContainer"/>) and must be built with
    /// <c>AllowAutoRedirect = false</c>: this backend refuses a redirect response, but a client that follows
    /// redirects never shows it one.  <see cref="Create(PasskeyConfiguration, PasskeyHttpBackendOptions?)"/> builds a
    /// client that does both.  <see cref="PasskeyHttpBackendOptions.Timeout"/> bounds each whole exchange, body
    /// included, whatever the client's own timeout.  The client is not disposed with the backend, and its cookies are
    /// the host's: after a sign-out, or a removal that ended the session (<see cref="PasskeySignOut.SignedOut"/>), the
    /// host should drop the session cookie itself.  A backend from <c>Create</c> does that on its own.
    /// </summary>
    /// <param name="httpClient">The client to send with.</param>
    /// <param name="configuration">The app's passkey configuration; validated here.</param>
    /// <param name="options">Where the endpoints are; null for the defaults.</param>
    /// <exception cref="ArgumentException">The configuration or an option is invalid.</exception>
    public HttpPasskeyBackend(HttpClient httpClient, PasskeyConfiguration configuration, PasskeyHttpBackendOptions? options = null)
        : this(httpClient, configuration, options, null)
    {
    }

    private HttpPasskeyBackend(HttpClient httpClient, PasskeyConfiguration configuration, PasskeyHttpBackendOptions? options, PasskeySessionCookieJar? ownedCookieJar)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        PasskeyHttpBackendOptions resolved = options ?? new PasskeyHttpBackendOptions();
        _accountEndpoint = resolved.Resolve(configuration.RelyingPartyId);
        _maxResponseBytes = resolved.MaxResponseBytes;
        _timeout = resolved.Timeout;
        _httpClient = httpClient;
        _ownsHttpClient = ownedCookieJar is not null;
        _cookieJar = ownedCookieJar;
    }

    /// <summary>The base the endpoint paths are resolved against, ending in <c>/</c>.</summary>
    public Uri AccountEndpoint => _accountEndpoint;

    /// <summary>
    /// Creates a backend with its own client: the platform's handler with cookies and redirects
    /// off, behind a cookie jar that keeps the session, and the configured timeout.  The jar is emptied when a sign-out
    /// or a removal ends the session, so the old cookie is never sent again.
    /// </summary>
    /// <param name="configuration">The app's passkey configuration; validated here.</param>
    /// <param name="options">Where the endpoints are; null for the defaults.</param>
    /// <returns>The backend; dispose it to release the client.</returns>
    /// <exception cref="ArgumentException">The configuration or an option is invalid.</exception>
    public static HttpPasskeyBackend Create(PasskeyConfiguration configuration, PasskeyHttpBackendOptions? options = null)
    {
        return Create(configuration, options, CreatePlatformHandler());
    }

    /// <summary>The platform handler <see cref="Create(PasskeyConfiguration, PasskeyHttpBackendOptions?)"/> sends through: cookies off (the jar keeps the session) and redirects off.</summary>
    /// <returns>The handler.</returns>
    internal static HttpClientHandler CreatePlatformHandler()
    {
        return new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false
        };
    }

    /// <summary>Creates a backend whose cookie jar sits in front of <paramref name="transport"/>; the seam tests use.</summary>
    internal static HttpPasskeyBackend Create(PasskeyConfiguration configuration, PasskeyHttpBackendOptions? options, HttpMessageHandler transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        PasskeyHttpBackendOptions resolved = options ?? new PasskeyHttpBackendOptions();
        PasskeySessionCookieJar jar = new PasskeySessionCookieJar(transport);
        HttpClient client = new HttpClient(jar, disposeHandler: true)
        {
            Timeout = resolved.Timeout
        };
        try
        {
            return new HttpPasskeyBackend(client, configuration, resolved, jar);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(PasskeyRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[] body = Body(writer => WriteSignUpForm(writer, request));
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.RegistrationBegin, Routes.RegistrationBegin, body, cancellationToken).ConfigureAwait(false);
        return new PasskeyRegistrationChallenge { Options = new WebAuthnCreationOptions(RequiredObject(root, "options", Routes.RegistrationBegin)) };
    }

    /// <inheritdoc />
    public async Task<PasskeySignIn> CompleteRegistrationAsync(PasskeyRegistrationRequest request, WebAuthnRegistrationResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        byte[] body = Body(writer =>
        {
            WriteSignUpForm(writer, request);
            WriteCredential(writer, response.Json);
        });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.RegistrationComplete, Routes.RegistrationComplete, body, cancellationToken).ConfigureAwait(false);
        return ReadSignIn(root, Routes.RegistrationComplete);
    }

    /// <inheritdoc />
    public async Task<WebAuthnCreationOptions> BeginAdditionalRegistrationAsync(CancellationToken cancellationToken = default)
    {
        byte[] body = Body(_ => { });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.AdditionalBegin, Routes.AdditionalBegin, body, cancellationToken).ConfigureAwait(false);
        return new WebAuthnCreationOptions(RequiredObject(root, "options", Routes.AdditionalBegin));
    }

    /// <inheritdoc />
    public async Task<string> CompleteAdditionalRegistrationAsync(WebAuthnRegistrationResponse response, string? name = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        byte[] body = Body(writer =>
        {
            WriteCredential(writer, response.Json);
            if (!string.IsNullOrWhiteSpace(name))
            {
                writer.WriteString("name", name);
            }
        });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.AdditionalComplete, Routes.AdditionalComplete, body, cancellationToken).ConfigureAwait(false);
        string credentialId = RequiredString(root, "credentialId", Routes.AdditionalComplete, MaxCredentialIdLength);
        if (!IsBase64Url(credentialId))
        {
            throw Invalid(Routes.AdditionalComplete, "has an unusable credentialId");
        }
        return credentialId;
    }

    /// <inheritdoc />
    public async Task<PasskeyAuthenticationChallenge> BeginAuthenticationAsync(string? handle, CancellationToken cancellationToken = default)
    {
        byte[] body = Body(writer =>
        {
            if (!string.IsNullOrWhiteSpace(handle))
            {
                writer.WriteString("handle", handle);
            }
        });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.AssertionBegin, Routes.AssertionBegin, body, cancellationToken).ConfigureAwait(false);
        return new PasskeyAuthenticationChallenge
        {
            Options = new WebAuthnRequestOptions(RequiredObject(root, "options", Routes.AssertionBegin)),
            CeremonyId = RequiredString(root, "ceremonyId", Routes.AssertionBegin, MaxCeremonyIdLength)
        };
    }

    /// <inheritdoc />
    public async Task<PasskeySignIn> CompleteAuthenticationAsync(PasskeyAuthenticationChallenge challenge, WebAuthnAssertionResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(response);
        if (string.IsNullOrWhiteSpace(challenge.CeremonyId))
        {
            throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, "The sign-in challenge has no ceremony id; the account endpoints always issue one at assertion/begin.");
        }
        byte[] body = Body(writer =>
        {
            writer.WriteString("ceremonyId", challenge.CeremonyId);
            WriteCredential(writer, response.Json);
        });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.AssertionComplete, Routes.AssertionComplete, body, cancellationToken).ConfigureAwait(false);
        return ReadSignIn(root, Routes.AssertionComplete);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PasskeyCredential>> GetPasskeysAsync(CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(HttpMethod.Get, Routes.Passkeys, Routes.Passkeys, null, cancellationToken).ConfigureAwait(false);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("passkeys", out JsonElement passkeys) || passkeys.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(Routes.Passkeys, "has no passkeys array");
        }
        if (passkeys.GetArrayLength() > MaxPasskeys)
        {
            throw Invalid(Routes.Passkeys, "lists more passkeys than the client accepts");
        }
        List<PasskeyCredential> credentials = new List<PasskeyCredential>();
        foreach (JsonElement passkey in passkeys.EnumerateArray())
        {
            if (passkey.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(Routes.Passkeys, "lists a passkey that is not an object");
            }
            credentials.Add(new PasskeyCredential
            {
                CredentialId = RequiredString(passkey, "id", Routes.Passkeys, MaxCredentialIdLength),
                Name = OptionalString(passkey, "name", Routes.Passkeys),
                CreatedUtc = OptionalDate(passkey, "createdUtc", Routes.Passkeys),
                LastUsedUtc = OptionalDate(passkey, "lastUsedUtc", Routes.Passkeys),
                IsCurrent = OptionalBool(passkey, "current", Routes.Passkeys),
                IsRegisteredKeySet = OptionalBool(passkey, "registeredKeySet", Routes.Passkeys)
            });
        }
        return credentials;
    }

    /// <inheritdoc />
    public Task<PasskeySignOut> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        return DeletePasskeyAsync(credentialId, PasskeyKeySetRemoval.Default, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PasskeySignOut> DeletePasskeyAsync(string credentialId, PasskeyKeySetRemoval keySet, CancellationToken cancellationToken = default)
    {
        RequireSendable(credentialId);
        // The endpoints refuse to remove the key-set passkey without ?keySet=keep on a host that can move key sets
        // (409 KeySetHandoverRequired), and ignore it everywhere it changes nothing.
        string path = Routes.Passkeys + "/" + credentialId + (keySet == PasskeyKeySetRemoval.KeepKeySet ? "?keySet=keep" : string.Empty);
        JsonElement root = await SendAsync(HttpMethod.Delete, path, Routes.PasskeyById, null, cancellationToken).ConfigureAwait(false);
        return Ended(ReadSignOut(root, Routes.PasskeyById));
    }

    /// <inheritdoc />
    public async Task<PasskeyHandoverChallenge> BeginKeySetHandoverAsync(string credentialId, string? heirCredentialId, CancellationToken cancellationToken = default)
    {
        RequireSendable(credentialId);
        if (heirCredentialId is not null)
        {
            RequireSendable(heirCredentialId);
        }
        byte[] body = Body(writer =>
        {
            if (heirCredentialId is not null)
            {
                writer.WriteString("heir", heirCredentialId);
            }
        });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.Passkeys + "/" + credentialId + "/handover/begin", Routes.HandoverBegin, body, cancellationToken).ConfigureAwait(false);
        return new PasskeyHandoverChallenge
        {
            Options = new WebAuthnRequestOptions(RequiredObject(root, "options", Routes.HandoverBegin)),
            HandoverId = RequiredString(root, "handoverId", Routes.HandoverBegin, MaxCeremonyIdLength)
        };
    }

    /// <inheritdoc />
    public async Task<PasskeySignOut> CompleteKeySetHandoverAsync(string credentialId, PasskeyHandoverChallenge challenge, WebAuthnAssertionResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(response);
        RequireSendable(credentialId);
        if (string.IsNullOrWhiteSpace(challenge.HandoverId))
        {
            throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, "The handover challenge has no handover id; the account endpoints always issue one at handover/begin.");
        }
        byte[] body = Body(writer =>
        {
            writer.WriteString("handoverId", challenge.HandoverId);
            WriteCredential(writer, response.Json);
        });
        JsonElement root = await SendAsync(HttpMethod.Post, Routes.Passkeys + "/" + credentialId + "/handover/complete", Routes.HandoverComplete, body, cancellationToken).ConfigureAwait(false);
        return Ended(ReadSignOut(root, Routes.HandoverComplete));
    }

    // A removal that ended the session (the session was opened with the removed passkey): the old cookie is never sent again.
    private PasskeySignOut Ended(PasskeySignOut signOut)
    {
        if (signOut.SignedOut)
        {
            _cookieJar?.Clear();
        }
        return signOut;
    }

    /// <inheritdoc />
    public async Task RenamePasskeyAsync(string credentialId, string? name, CancellationToken cancellationToken = default)
    {
        RequireSendable(credentialId);
        byte[] body = Body(writer =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                writer.WriteNull("name");
            }
            else
            {
                writer.WriteString("name", name);
            }
        });
        await SendAsync(HttpMethod.Post, Routes.Passkeys + "/" + credentialId + "/name", Routes.PasskeyName, body, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PasskeySignOut> SignOutAsync(CancellationToken cancellationToken = default)
    {
        byte[] body = Body(_ => { });
        try
        {
            JsonElement root = await SendAsync(HttpMethod.Post, Routes.SignOut, Routes.SignOut, body, cancellationToken).ConfigureAwait(false);
            return ReadSignOut(root, Routes.SignOut);
        }
        finally
        {
            // The person asked to leave: whatever the server says, or if it never answered, this client stops sending
            // the cookie.  A server that never got the request keeps the session until it expires, but this device no
            // longer holds it.
            _cookieJar?.Clear();
        }
    }

    /// <inheritdoc />
    public async Task SaveProfileAsync(PasskeyProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        byte[] body = Body(writer =>
        {
            writer.WriteString("displayName", profile.DisplayName);
            if (!string.IsNullOrWhiteSpace(profile.Email))
            {
                writer.WriteString("email", profile.Email);
            }
        });
        await SendAsync(HttpMethod.Post, Routes.Profile, Routes.Profile, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the client when the backend created it.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>The category for a refused request, by the endpoint's <c>error</c> code first and the HTTP status second.</summary>
    /// <param name="status">The response status.</param>
    /// <param name="backendCode">The endpoint's <c>error</c> code, or null.</param>
    /// <returns>The category.</returns>
    internal static PasskeyErrorCategory CategoryFor(HttpStatusCode status, string? backendCode)
    {
        int code = (int)status;
        if (code is >= 300 and < 400)
        {
            return PasskeyErrorCategory.InvalidServerResponse;
        }
        switch (backendCode)
        {
            case BackendCodes.NotSignedIn:
                return PasskeyErrorCategory.NotSignedIn;
            case BackendCodes.RateLimited:
                return PasskeyErrorCategory.RateLimited;
            case BackendCodes.BadRequest:
            case BackendCodes.InvalidHandle:
            case BackendCodes.HandleTaken:
            case BackendCodes.EmailRequired:
            case BackendCodes.InviteRequired:
            case BackendCodes.NotFound:
            case BackendCodes.LastPasskey:
            case BackendCodes.LastCredential:
            case BackendCodes.KeySetHandoverRequired:
            case BackendCodes.InvalidHeir:
            case BackendCodes.NotKeySet:
            case BackendCodes.RotationUnavailable:
            case BackendCodes.KeySetChanged:
            case BackendCodes.HeirGone:
                // The account's passkeys aren't what the request assumed; the screen reads the code to say which.
                return PasskeyErrorCategory.InvalidOptions;
            case BackendCodes.HandoverExpired:
                return PasskeyErrorCategory.ExpiredChallenge;
            case BackendCodes.ProofRefused:
                return PasskeyErrorCategory.ServerVerificationFailed;
            case BackendCodes.RequestRefused:
            case BackendCodes.SignInFailed:
            case BackendCodes.SignUpFailed:
            case BackendCodes.SignUpClosed:
            case BackendCodes.PasskeyNotAdded:
                return PasskeyErrorCategory.ServerVerificationFailed;
            case BackendCodes.ProfileNotSaved:
                // The server couldn't store it: nothing the person sent was wrong, and nothing failed verification.
                return PasskeyErrorCategory.Unknown;
        }
        return code switch
        {
            401 => PasskeyErrorCategory.NotSignedIn,
            429 => PasskeyErrorCategory.RateLimited,
            400 => PasskeyErrorCategory.InvalidOptions,
            404 or 405 => PasskeyErrorCategory.InvalidServerResponse,
            502 or 503 or 504 => PasskeyErrorCategory.NetworkFailure,
            >= 500 => PasskeyErrorCategory.InvalidServerResponse,
            >= 400 => PasskeyErrorCategory.ServerVerificationFailed,
            _ => PasskeyErrorCategory.InvalidServerResponse
        };
    }

    /// <summary>The server's suggested next location, kept only when it is a path on the site (<c>/x</c>, not <c>//x</c>, not absolute, no backslash or control character).</summary>
    /// <param name="nextUrl">What the server sent.</param>
    /// <returns>The path, or null.</returns>
    internal static string? SitePathOrNull(string? nextUrl)
    {
        if (string.IsNullOrEmpty(nextUrl) || nextUrl.Length > 2048 || nextUrl[0] != '/')
        {
            return null;
        }
        if (nextUrl.Length > 1 && (nextUrl[1] == '/' || nextUrl[1] == '\\'))
        {
            return null;
        }
        foreach (char character in nextUrl)
        {
            if (character == '\\' || char.IsControl(character))
            {
                return null;
            }
        }
        return nextUrl;
    }

    /// <summary>Whether a credential id is non-empty unpadded base64url of a plausible length.</summary>
    /// <param name="credentialId">The id.</param>
    /// <returns>True when it may go into a path.</returns>
    internal static bool IsBase64Url(string? credentialId)
    {
        if (string.IsNullOrEmpty(credentialId) || credentialId.Length > MaxCredentialIdLength)
        {
            return false;
        }
        foreach (char character in credentialId)
        {
            bool allowed = character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
            if (!allowed)
            {
                return false;
            }
        }
        return true;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, string route, byte[]? body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(method, new Uri(_accountEndpoint, path));
        request.Headers.TryAddWithoutValidation(RequestHeaderName, RequestHeaderValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            ByteArrayContent content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Content = content;
        }

        // The timeout covers the whole exchange: a server that sends headers and then stalls the body is cut off too.
        using CancellationTokenSource exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exchange.CancelAfter(_timeout);
        HttpResponseMessage? received = null;
        byte[] payload;
        try
        {
            received = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, exchange.Token).ConfigureAwait(false);
            payload = await ReadCappedAsync(received, route, exchange.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            received?.Dispose();
            throw new PasskeyException(PasskeyErrorCategory.NetworkFailure, $"The account endpoint {route} did not answer within {_timeout.TotalSeconds:0.#} s.");
        }
        catch (Exception failed) when ((failed is HttpRequestException || failed is IOException) && cancellationToken.IsCancellationRequested)
        {
            // A host's content stream may fail with a plain IOException once the caller cancels: that is a cancellation.
            received?.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception failed) when (failed is HttpRequestException || failed is IOException)
        {
            // A refused connection, a TLS failure or a connection dropped mid-body (HttpIOException is an IOException).
            // The inner message can quote hosts and socket details, so it is kept as the inner exception, not quoted.
            received?.Dispose();
            throw new PasskeyException(PasskeyErrorCategory.NetworkFailure, $"The account endpoint {route} could not be reached.", failed);
        }
        catch
        {
            received?.Dispose();
            throw;
        }
        using HttpResponseMessage response = received;
        if (!response.IsSuccessStatusCode)
        {
            string? backendCode = BackendCodeOf(payload);
            PasskeyErrorCategory category = CategoryFor(response.StatusCode, backendCode);
            throw new PasskeyException(category, $"The account endpoint {route} refused the request: HTTP {(int)response.StatusCode}, code {backendCode ?? "none"}.", null, backendCode);
        }
        if (payload.Length == 0)
        {
            return default;
        }
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !mediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(route, "is not JSON");
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(route, "is not a JSON object");
            }
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Invalid(route, "is not JSON");
        }
    }

    private async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, string route, CancellationToken cancellationToken)
    {
        long? declared = response.Content.Headers.ContentLength;
        if (declared > _maxResponseBytes)
        {
            throw Invalid(route, "is larger than the client accepts");
        }
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > _maxResponseBytes)
            {
                throw Invalid(route, "is larger than the client accepts");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string? BackendCodeOf(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            string? code = error.GetString();
            if (string.IsNullOrEmpty(code) || code.Length > MaxBackendCodeLength)
            {
                return null;
            }
            foreach (char character in code)
            {
                if (!char.IsAsciiLetterOrDigit(character))
                {
                    return null;
                }
            }
            return code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PasskeySignIn ReadSignIn(JsonElement root, string route)
    {
        return new PasskeySignIn
        {
            Handle = RequiredString(root, "handle", route, 256),
            DisplayName = OptionalString(root, "displayName", route),
            NextUrl = SitePathOrNull(OptionalString(root, "nextUrl", route)),
            ProfileCreated = OptionalBool(root, "profileCreated", route) ?? true
        };
    }

    private static PasskeySignOut ReadSignOut(JsonElement root, string route)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("signedOut", out JsonElement signedOut)
            || (signedOut.ValueKind != JsonValueKind.True && signedOut.ValueKind != JsonValueKind.False))
        {
            throw Invalid(route, "has no signedOut");
        }
        return new PasskeySignOut
        {
            SignedOut = signedOut.GetBoolean(),
            NextUrl = SitePathOrNull(OptionalString(root, "nextUrl", route))
        };
    }

    private static void RequireSendable(string credentialId)
    {
        if (!IsBase64Url(credentialId))
        {
            throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, "The credential id is not base64url, so it was not sent.");
        }
    }

    private static bool? OptionalBool(JsonElement root, string name, string route)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid(route, "has a " + name + " that is not true or false")
        };
    }

    private static string RequiredObject(JsonElement root, string name, string route)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(route, "has no " + name + " object");
        }
        return value.GetRawText();
    }

    private static string RequiredString(JsonElement root, string name, string route, int maxLength)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(route, "has no " + name);
        }
        string text = value.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength)
        {
            throw Invalid(route, "has an unusable " + name);
        }
        return text;
    }

    private static string? OptionalString(JsonElement root, string name, string route)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(route, "has a " + name + " that is not a string");
        }
        return value.GetString();
    }

    private static DateTime? OptionalDate(JsonElement root, string name, string route)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || !value.TryGetDateTime(out DateTime date))
        {
            throw Invalid(route, "has a " + name + " that is not a date");
        }
        return date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }

    private static PasskeyException Invalid(string route, string what)
    {
        return new PasskeyException(PasskeyErrorCategory.InvalidServerResponse, $"The account endpoint {route} response {what}.");
    }

    private static byte[] Body(Action<Utf8JsonWriter> members)
    {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            members(writer);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSignUpForm(Utf8JsonWriter writer, PasskeyRegistrationRequest request)
    {
        writer.WriteString("handle", request.Handle);
        writer.WriteString("displayName", request.DisplayName);
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            writer.WriteString("email", request.Email);
        }
        if (!string.IsNullOrWhiteSpace(request.InviteCode))
        {
            writer.WriteString("inviteCode", request.InviteCode);
        }
    }

    private static void WriteCredential(Utf8JsonWriter writer, string responseJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The authenticator's response is not a JSON object.");
            }
        }
        catch (JsonException)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The authenticator's response is not JSON.");
        }
        writer.WritePropertyName("credential");
        writer.WriteRawValue(responseJson);
    }

    private static class Routes
    {
        internal const string RegistrationBegin = "passkey/registration/begin";
        internal const string RegistrationComplete = "passkey/registration/complete";
        internal const string AdditionalBegin = "passkey/additional/begin";
        internal const string AdditionalComplete = "passkey/additional/complete";
        internal const string AssertionBegin = "passkey/assertion/begin";
        internal const string AssertionComplete = "passkey/assertion/complete";
        internal const string Passkeys = "passkeys";
        internal const string PasskeyById = "passkeys/{id}";
        internal const string PasskeyName = "passkeys/{id}/name";
        internal const string HandoverBegin = "passkeys/{id}/handover/begin";
        internal const string HandoverComplete = "passkeys/{id}/handover/complete";
        internal const string SignOut = "signout";
        internal const string Profile = "profile";
    }

    /// <summary>The <c>AccountErrorCodes</c> of the account endpoints, as they appear on the wire.</summary>
    internal static class BackendCodes
    {
        internal const string BadRequest = "BadRequest";
        internal const string RequestRefused = "RequestRefused";
        internal const string NotSignedIn = "NotSignedIn";
        internal const string SignUpClosed = "SignUpClosed";
        internal const string InviteRequired = "InviteRequired";
        internal const string InvalidHandle = "InvalidHandle";
        internal const string HandleTaken = "HandleTaken";
        internal const string EmailRequired = "EmailRequired";
        internal const string SignUpFailed = "SignUpFailed";
        internal const string SignInFailed = "SignInFailed";
        internal const string PasskeyNotAdded = "PasskeyNotAdded";
        internal const string NotFound = "NotFound";
        internal const string LastPasskey = "LastPasskey";
        internal const string RateLimited = "RateLimited";
        internal const string ProfileNotSaved = "ProfileNotSaved";
        internal const string KeySetHandoverRequired = "KeySetHandoverRequired";
        internal const string LastCredential = "LastCredential";
        internal const string InvalidHeir = "InvalidHeir";
        internal const string NotKeySet = "NotKeySet";
        internal const string RotationUnavailable = "RotationUnavailable";
        internal const string HandoverExpired = "HandoverExpired";
        internal const string ProofRefused = "ProofRefused";
        internal const string KeySetChanged = "KeySetChanged";
        internal const string HeirGone = "HeirGone";
    }
}
