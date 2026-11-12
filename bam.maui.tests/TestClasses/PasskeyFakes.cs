using Bam.Maui.Passkeys;

namespace Bam.Maui.Tests.TestClasses;

/// <summary>A shared, ordered record of which collaborator calls a test triggered.</summary>
public sealed class CallLog
{
    public List<string> Calls { get; } = new List<string>();

    public void Add(string call)
    {
        Calls.Add(call);
    }

    public string Joined => string.Join(",", Calls);
}

/// <summary>A backend whose answers and failures a test sets up front.</summary>
public sealed class FakePasskeyBackend : IPasskeyBackend
{
    private readonly CallLog _log;

    public FakePasskeyBackend(CallLog log)
    {
        _log = log;
    }

    public Dictionary<string, Exception> Failures { get; } = new Dictionary<string, Exception>();
    public string? NormalizedHandle { get; set; }
    public string? CeremonyId { get; set; } = "ceremony-1";
    public PasskeySignIn? SignIn { get; set; } = new PasskeySignIn { Handle = "ada", DisplayName = "Ada" };
    public List<PasskeyCredential> Credentials { get; set; } = new List<PasskeyCredential>();
    public bool ReturnNullOptions { get; set; }
    public Action<string>? BeforeCall { get; set; }

    public PasskeyRegistrationRequest? CompletedRegistration { get; private set; }
    public string? BegunHandle { get; private set; }
    public PasskeyAuthenticationChallenge? CompletedChallenge { get; private set; }
    public string? CompletedAssertionJson { get; private set; }
    public string? CompletedRegistrationJson { get; private set; }
    public string? DeletedCredentialId { get; private set; }
    public string? AddedName { get; private set; }
    public string AddedCredentialId { get; set; } = "added-1";
    public PasskeySignOut DeleteResult { get; set; } = new PasskeySignOut { SignedOut = false };
    public PasskeySignOut SignOutResult { get; set; } = new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" };
    public string? RenamedCredentialId { get; private set; }
    public string? RenamedTo { get; private set; }
    public PasskeyProfile? SavedProfile { get; private set; }

    public Task<PasskeyRegistrationChallenge> BeginRegistrationAsync(PasskeyRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        Enter("backend.beginRegistration");
        PasskeyRegistrationChallenge challenge = ReturnNullOptions
            ? null!
            : new PasskeyRegistrationChallenge { Options = new WebAuthnCreationOptions("{\"challenge\":\"reg\"}"), Handle = NormalizedHandle };
        return Task.FromResult(challenge);
    }

    public Task<PasskeySignIn> CompleteRegistrationAsync(PasskeyRegistrationRequest request, WebAuthnRegistrationResponse response, CancellationToken cancellationToken = default)
    {
        Enter("backend.completeRegistration");
        CompletedRegistration = request;
        CompletedRegistrationJson = response.Json;
        return Task.FromResult(SignIn!);
    }

    public Task<WebAuthnCreationOptions> BeginAdditionalRegistrationAsync(CancellationToken cancellationToken = default)
    {
        Enter("backend.beginAdditional");
        return Task.FromResult(new WebAuthnCreationOptions("{\"challenge\":\"add\"}"));
    }

    public Task<string> CompleteAdditionalRegistrationAsync(WebAuthnRegistrationResponse response, string? name = null, CancellationToken cancellationToken = default)
    {
        Enter("backend.completeAdditional");
        CompletedRegistrationJson = response.Json;
        AddedName = name;
        return Task.FromResult(AddedCredentialId);
    }

    public Task<PasskeyAuthenticationChallenge> BeginAuthenticationAsync(string? handle, CancellationToken cancellationToken = default)
    {
        Enter("backend.beginAuthentication");
        BegunHandle = handle;
        PasskeyAuthenticationChallenge challenge = ReturnNullOptions
            ? null!
            : new PasskeyAuthenticationChallenge { Options = new WebAuthnRequestOptions("{\"challenge\":\"auth\"}"), CeremonyId = CeremonyId };
        return Task.FromResult(challenge);
    }

    public Task<PasskeySignIn> CompleteAuthenticationAsync(PasskeyAuthenticationChallenge challenge, WebAuthnAssertionResponse response, CancellationToken cancellationToken = default)
    {
        Enter("backend.completeAuthentication");
        CompletedChallenge = challenge;
        CompletedAssertionJson = response.Json;
        return Task.FromResult(SignIn!);
    }

    public Task<IReadOnlyList<PasskeyCredential>> GetPasskeysAsync(CancellationToken cancellationToken = default)
    {
        Enter("backend.getPasskeys");
        IReadOnlyList<PasskeyCredential> credentials = Credentials;
        return Task.FromResult(credentials);
    }

    public Task<PasskeySignOut> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        Enter("backend.deletePasskey");
        DeletedCredentialId = credentialId;
        return Task.FromResult(DeleteResult);
    }

    public Task RenamePasskeyAsync(string credentialId, string? name, CancellationToken cancellationToken = default)
    {
        Enter("backend.renamePasskey");
        RenamedCredentialId = credentialId;
        RenamedTo = name;
        return Task.CompletedTask;
    }

    public Task<PasskeySignOut> SignOutAsync(CancellationToken cancellationToken = default)
    {
        Enter("backend.signOut");
        return Task.FromResult(SignOutResult);
    }

    public Task SaveProfileAsync(PasskeyProfile profile, CancellationToken cancellationToken = default)
    {
        Enter("backend.saveProfile");
        SavedProfile = profile;
        return Task.CompletedTask;
    }

    private void Enter(string call)
    {
        _log.Add(call);
        BeforeCall?.Invoke(call);
        if (Failures.TryGetValue(call, out Exception? failure))
        {
            throw failure;
        }
    }
}

/// <summary>An authenticator whose capabilities, answers and failures a test sets up front.</summary>
public sealed class FakePasskeyAuthenticator : IPasskeyAuthenticator
{
    private readonly CallLog _log;

    public FakePasskeyAuthenticator(CallLog log)
    {
        _log = log;
    }

    public PasskeyCapabilities Capabilities { get; set; } = new PasskeyCapabilities
    {
        Availability = PasskeyAvailability.Available,
        SupportsDiscoverableCredentials = true,
        Platform = "Test"
    };

    public Exception? CapabilitiesFailure { get; set; }
    public Func<CancellationToken, Task<PasskeyCapabilities>>? CapabilitiesProbe { get; set; }
    public Exception? CreateFailure { get; set; }
    public Exception? AssertFailure { get; set; }
    public string? CreatedWithJson { get; private set; }
    public string? AssertedWithJson { get; private set; }

    public Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (CapabilitiesFailure is not null)
        {
            throw CapabilitiesFailure;
        }
        if (CapabilitiesProbe is not null)
        {
            return CapabilitiesProbe(cancellationToken);
        }
        return Task.FromResult(Capabilities);
    }

    public Task<WebAuthnRegistrationResponse> CreateAsync(WebAuthnCreationOptions options, CancellationToken cancellationToken = default)
    {
        _log.Add("authenticator.create");
        CreatedWithJson = options.Json;
        if (CreateFailure is not null)
        {
            throw CreateFailure;
        }
        return Task.FromResult(new WebAuthnRegistrationResponse("{\"id\":\"created\"}"));
    }

    public Task<WebAuthnAssertionResponse> AssertAsync(WebAuthnRequestOptions options, CancellationToken cancellationToken = default)
    {
        _log.Add("authenticator.assert");
        AssertedWithJson = options.Json;
        if (AssertFailure is not null)
        {
            throw AssertFailure;
        }
        return Task.FromResult(new WebAuthnAssertionResponse("{\"id\":\"asserted\"}"));
    }
}

/// <summary>Everything a client test needs, wired together.</summary>
public sealed class PasskeyClientFixture
{
    public PasskeyClientFixture()
    {
        Log = new CallLog();
        Backend = new FakePasskeyBackend(Log);
        Authenticator = new FakePasskeyAuthenticator(Log);
        Configuration = ValidConfiguration();
    }

    public CallLog Log { get; }
    public FakePasskeyBackend Backend { get; }
    public FakePasskeyAuthenticator Authenticator { get; }
    public PasskeyConfiguration Configuration { get; }

    public PasskeyClient Client()
    {
        return new PasskeyClient(Backend, Authenticator, Configuration, new PasskeyErrorNormalizer(Configuration.Messages));
    }

    public static PasskeyConfiguration ValidConfiguration()
    {
        return new PasskeyConfiguration
        {
            RelyingPartyId = "example.com",
            Branding = new PasskeyBranding { ApplicationName = "Example" }
        };
    }
}
