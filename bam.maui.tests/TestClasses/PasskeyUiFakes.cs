using Bam.Maui.Passkeys;

namespace Bam.Maui.Tests.TestClasses;

/// <summary>Records where the screens asked to go.</summary>
public sealed class FakePasskeyNavigator : IPasskeyNavigator
{
    public List<string> Calls { get; } = new List<string>();
    public PasskeySignIn? SignedIn { get; private set; }
    public PasskeyFlow? SignedInFrom { get; private set; }
    public string Joined => string.Join(",", Calls);

    public Task SignedInAsync(PasskeySignIn signIn, PasskeyFlow flow)
    {
        Calls.Add("signedIn");
        SignedIn = signIn;
        SignedInFrom = flow;
        return Task.CompletedTask;
    }

    public Task AlternativeSignInRequestedAsync()
    {
        Calls.Add("alternative");
        return Task.CompletedTask;
    }

    public Task RegistrationRequestedAsync()
    {
        Calls.Add("registration");
        return Task.CompletedTask;
    }

    public Task SignInRequestedAsync()
    {
        Calls.Add("signIn");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A passkey client whose sign-in calls return what a test scripts.  A gate holds a call open so a test can look at
/// the screen while the ceremony is running, and cancel it.
/// </summary>
public sealed class ScriptedPasskeyClient : IPasskeyClient
{
    public List<string> Calls { get; } = new List<string>();
    public string? LastHandle { get; private set; }
    public PasskeyRegistrationRequest? LastRequest { get; private set; }
    public PasskeyCapabilities Capabilities { get; set; } = new PasskeyCapabilities { Availability = PasskeyAvailability.Available, SupportsDiscoverableCredentials = true, Platform = "Test" };
    public Queue<PasskeyResult<PasskeySignIn>> SignInResults { get; } = new Queue<PasskeyResult<PasskeySignIn>>();
    public TaskCompletionSource<bool>? Gate { get; set; }

    public static PasskeySignIn Ada { get; } = new PasskeySignIn { Handle = "ada", DisplayName = "Ada" };

    public static PasskeyResult<PasskeySignIn> Failed(PasskeyErrorCategory category)
    {
        return PasskeyResult<PasskeySignIn>.Failure(new PasskeyError(category, "message for " + category));
    }

    public Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Capabilities.IsSupported);
    }

    public Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("capabilities");
        return Task.FromResult(Capabilities);
    }

    public Task<PasskeyResult<PasskeySignIn>> RegisterPasskeyAsync(PasskeyRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add("register");
        LastRequest = request;
        return NextAsync(cancellationToken);
    }

    public Task<PasskeyResult> AddPasskeyAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("not used by the sign-in screens");
    }

    public Task<PasskeyResult<PasskeySignIn>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("authenticate");
        return NextAsync(cancellationToken);
    }

    public Task<PasskeyResult<PasskeySignIn>> AuthenticateWithUsernameAsync(string handle, CancellationToken cancellationToken = default)
    {
        Calls.Add("authenticateWithUsername");
        LastHandle = handle;
        return NextAsync(cancellationToken);
    }

    public Task<PasskeyResult<IReadOnlyList<PasskeyCredential>>> GetRegisteredPasskeysAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("not used by the sign-in screens");
    }

    public Task<PasskeyResult> DeletePasskeyAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("not used by the sign-in screens");
    }

    public Task<PasskeyStatus> GetPasskeyStatusAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("not used by the sign-in screens");
    }

    private async Task<PasskeyResult<PasskeySignIn>> NextAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? gate = Gate;
        if (gate is not null)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            await gate.Task;
        }
        return SignInResults.Count > 0 ? SignInResults.Dequeue() : PasskeyResult<PasskeySignIn>.Success(Ada);
    }
}
