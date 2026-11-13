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

    /// <summary>What the last SignedOutAsync was told.</summary>
    public PasskeySignOut? SignedOut { get; private set; }

    /// <summary>Thrown by SignedOutAsync while set.</summary>
    public Exception? SignedOutFailure { get; set; }

    /// <summary>Runs inside SignedOutAsync, while the screen is still on its signed-out hand-off.</summary>
    public Action? WhileSignedOut { get; set; }

    public Task SignedOutAsync(PasskeySignOut signOut)
    {
        Calls.Add("signedOut");
        WhileSignedOut?.Invoke();
        if (SignedOutFailure is not null)
        {
            return Task.FromException(SignedOutFailure);
        }
        SignedOut = signOut;
        return Task.CompletedTask;
    }
}

/// <summary>A navigator that leaves SignedOutAsync to the interface default, as an app written before it would.</summary>
public sealed class OlderPasskeyNavigator : IPasskeyNavigator
{
    public List<string> Calls { get; } = new List<string>();

    public Task SignedInAsync(PasskeySignIn signIn, PasskeyFlow flow)
    {
        Calls.Add("signedIn");
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

    /// <summary>What GetRegisteredPasskeysAsync returns, one per call; the last one repeats.</summary>
    public Queue<PasskeyResult<IReadOnlyList<PasskeyCredential>>> ListResults { get; } = new Queue<PasskeyResult<IReadOnlyList<PasskeyCredential>>>();
    public PasskeyResult<string> AddResult { get; set; } = PasskeyResult<string>.Success("added-1");
    public PasskeyResult RenameResult { get; set; } = PasskeyResult.Success();
    public PasskeyResult<PasskeySignOut> DeleteResult { get; set; } = PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = false });
    public PasskeyResult<PasskeySignOut> SignOutResult { get; set; } = PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" });
    public PasskeyResult SaveProfileResult { get; set; } = PasskeyResult.Success();
    public string? AddedName { get; private set; }
    public string? RenamedId { get; private set; }
    public string? RenamedTo { get; private set; }
    public string? DeletedId { get; private set; }
    /// <summary>What DeletePasskeyAsync returns, one per call, before falling back to DeleteResult.</summary>
    public Queue<PasskeyResult<PasskeySignOut>> DeleteResults { get; } = new Queue<PasskeyResult<PasskeySignOut>>();
    public PasskeyResult<PasskeySignOut> HandoverResult { get; set; } = PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = false });
    /// <summary>What HandOverKeySetAsync returns, one per call, before falling back to HandoverResult.</summary>
    public Queue<PasskeyResult<PasskeySignOut>> HandoverResults { get; } = new Queue<PasskeyResult<PasskeySignOut>>();
    public string? HandoverId { get; private set; }
    public string? HandoverHeir { get; private set; }
    public PasskeyProfile? SavedProfile { get; private set; }

    public static PasskeyCredential Phone { get; } = new PasskeyCredential { CredentialId = "phone", Name = "Phone", CreatedUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), LastUsedUtc = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), IsCurrent = true, IsRegisteredKeySet = true };
    public static PasskeyCredential Laptop { get; } = new PasskeyCredential { CredentialId = "laptop", CreatedUtc = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), IsCurrent = false, IsRegisteredKeySet = false };

    public static PasskeyResult<IReadOnlyList<PasskeyCredential>> Listed(params PasskeyCredential[] credentials)
    {
        return PasskeyResult<IReadOnlyList<PasskeyCredential>>.Success(credentials);
    }

    public static PasskeyError Error(PasskeyErrorCategory category, string? backendCode = null)
    {
        return new PasskeyError(category, "message for " + category, backendCode);
    }

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

    public async Task<PasskeyResult<string>> AddPasskeyAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        Calls.Add("add");
        AddedName = name;
        await WaitAsync(cancellationToken);
        return AddResult;
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

    public async Task<PasskeyResult<IReadOnlyList<PasskeyCredential>>> GetRegisteredPasskeysAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        await WaitAsync(cancellationToken);
        if (ListResults.Count == 0)
        {
            return Listed(Phone, Laptop);
        }
        return ListResults.Count > 1 ? ListResults.Dequeue() : ListResults.Peek();
    }

    public async Task<PasskeyResult<PasskeySignOut>> DeletePasskeyAsync(string credentialId, PasskeyKeySetRemoval keySet, CancellationToken cancellationToken = default)
    {
        Calls.Add(keySet == PasskeyKeySetRemoval.KeepKeySet ? "deleteKeepingKeySet" : "delete");
        DeletedId = credentialId;
        await WaitAsync(cancellationToken);
        return DeleteResults.Count > 0 ? DeleteResults.Dequeue() : DeleteResult;
    }

    public async Task<PasskeyResult<PasskeySignOut>> HandOverKeySetAsync(string credentialId, string? heirCredentialId = null, CancellationToken cancellationToken = default)
    {
        Calls.Add("handover");
        HandoverId = credentialId;
        HandoverHeir = heirCredentialId;
        await WaitAsync(cancellationToken);
        return HandoverResults.Count > 0 ? HandoverResults.Dequeue() : HandoverResult;
    }

    public async Task<PasskeyResult> RenamePasskeyAsync(string credentialId, string? name, CancellationToken cancellationToken = default)
    {
        Calls.Add("rename");
        RenamedId = credentialId;
        RenamedTo = name;
        await WaitAsync(cancellationToken);
        return RenameResult;
    }

    public async Task<PasskeyResult<PasskeySignOut>> SignOutAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("signOut");
        await WaitAsync(cancellationToken);
        return SignOutResult;
    }

    public async Task<PasskeyResult> SaveProfileAsync(PasskeyProfile profile, CancellationToken cancellationToken = default)
    {
        Calls.Add("saveProfile");
        SavedProfile = profile;
        await WaitAsync(cancellationToken);
        return SaveProfileResult;
    }

    public Task<PasskeyStatus> GetPasskeyStatusAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("not used by the sign-in screens");
    }

    private async Task<PasskeyResult<PasskeySignIn>> NextAsync(CancellationToken cancellationToken)
    {
        await WaitAsync(cancellationToken);
        return SignInResults.Count > 0 ? SignInResults.Dequeue() : PasskeyResult<PasskeySignIn>.Success(Ada);
    }

    private async Task WaitAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? gate = Gate;
        if (gate is not null)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            await gate.Task;
        }
    }
}
