using Bam.Maui.Passkeys;

namespace Bam.Maui.Tests.TestClasses;

/// <summary>Records where the screens asked to go.</summary>
public sealed class FakePasskeyNavigator : IPasskeyNavigator
{
    public List<string> Calls { get; } = new List<string>();
    public PasskeySignIn? SignedIn { get; private set; }
    public PasskeyFlow? SignedInFrom { get; private set; }
    public string Joined => string.Join(",", Calls);

    /// <summary>Thrown by SignedInAsync while set, as an app's broken navigation would.</summary>
    public Exception? SignedInFailure { get; set; }

    /// <summary>Thrown by RegistrationRequestedAsync while set.</summary>
    public Exception? RegistrationFailure { get; set; }

    /// <summary>Thrown by AlternativeSignInRequestedAsync while set.</summary>
    public Exception? AlternativeFailure { get; set; }

    /// <summary>Holds AlternativeSignInRequestedAsync open until a test completes it, as a slow page push would.</summary>
    public TaskCompletionSource? AlternativeGate { get; set; }

    /// <summary>Runs inside SignedInAsync, while the screen is still on its success state.</summary>
    public Action? WhileSignedIn { get; set; }

    public Task SignedInAsync(PasskeySignIn signIn, PasskeyFlow flow)
    {
        Calls.Add("signedIn");
        if (SignedInFailure is not null)
        {
            return Task.FromException(SignedInFailure);
        }
        SignedIn = signIn;
        SignedInFrom = flow;
        WhileSignedIn?.Invoke();
        return Task.CompletedTask;
    }

    public async Task AlternativeSignInRequestedAsync()
    {
        Calls.Add("alternative");
        if (AlternativeGate is not null)
        {
            await AlternativeGate.Task;
        }
        if (AlternativeFailure is not null)
        {
            throw AlternativeFailure;
        }
    }

    public Task RegistrationRequestedAsync()
    {
        Calls.Add("registration");
        if (RegistrationFailure is not null)
        {
            throw RegistrationFailure;
        }
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
