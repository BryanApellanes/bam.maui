using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyService Should", Selector = "pksv")]
public class PasskeyServiceShould : UnitTestMenuContainer
{
    public PasskeyServiceShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void RegisterThroughBackendAndAuthenticator()
    {
        After.Setup(reg => reg.Set(new PasskeyServiceFixture()))
        .When<PasskeyServiceFixture>("registers a passkey with a handle the backend normalizes", (fixture, reg) =>
        {
            fixture.Backend.NormalizedHandle = "ada";
            PasskeyRegistrationRequest request = new PasskeyRegistrationRequest { Handle = "Ada", DisplayName = "Ada L", Email = "ada@example.com", InviteCode = "inv" };
            PasskeyResult<PasskeySignIn> result = Wait(fixture.Service().RegisterPasskeyAsync(request));
            return new RegistrationOutcome(
                result.Succeeded,
                result.Value?.Handle,
                fixture.Log.Joined,
                fixture.Authenticator.CreatedWithJson,
                fixture.Backend.CompletedRegistrationJson,
                fixture.Backend.CompletedRegistration?.Handle,
                fixture.Backend.CompletedRegistration?.Email,
                fixture.Backend.CompletedRegistration?.InviteCode);
        })
        .TheTest
        .ShouldPass<RegistrationOutcome>((because, outcome) =>
        {
            because.ItsTrue("registration succeeds", outcome.Succeeded);
            because.ItsTrue("the signed-in handle comes from the backend", outcome.Handle == "ada");
            because.ItsTrue("begin, native create, complete run in order", outcome.Calls == "backend.beginRegistration,authenticator.create,backend.completeRegistration");
            because.ItsTrue("the authenticator receives the backend's options unchanged", outcome.CreatedWith == "{\"challenge\":\"reg\"}");
            because.ItsTrue("the backend receives the authenticator's response unchanged", outcome.CompletedWith == "{\"id\":\"created\"}");
            because.ItsTrue("completion uses the normalized handle", outcome.CompletedHandle == "ada");
            because.ItsTrue("completion re-sends the email", outcome.CompletedEmail == "ada@example.com");
            because.ItsTrue("completion re-sends the invite code", outcome.CompletedInvite == "inv");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SignInWithAndWithoutUsername()
    {
        After.Setup(reg => reg.Set(new PasskeyServiceFixture()))
        .When<PasskeyServiceFixture>("signs in discoverably, then username-first", (fixture, reg) =>
        {
            PasskeyResult<PasskeySignIn> discoverable = Wait(fixture.Service().AuthenticateAsync());
            string? discoverableHandle = fixture.Backend.BegunHandle;
            string discoverableCalls = fixture.Log.Joined;
            fixture.Log.Calls.Clear();
            PasskeyResult<PasskeySignIn> named = Wait(fixture.Service().AuthenticateWithUsernameAsync("ada"));
            return new SignInOutcome(
                discoverable.Succeeded,
                discoverableHandle,
                discoverableCalls,
                named.Succeeded,
                fixture.Backend.BegunHandle,
                fixture.Backend.CompletedChallenge?.CeremonyId,
                fixture.Authenticator.AssertedWithJson,
                fixture.Backend.CompletedAssertionJson,
                named.Value?.Handle);
        })
        .TheTest
        .ShouldPass<SignInOutcome>((because, outcome) =>
        {
            because.ItsTrue("username-less sign-in succeeds", outcome.DiscoverableSucceeded);
            because.ItsTrue("username-less sign-in begins with no handle", outcome.DiscoverableHandle is null);
            because.ItsTrue("begin, native assert, complete run in order", outcome.DiscoverableCalls == "backend.beginAuthentication,authenticator.assert,backend.completeAuthentication");
            because.ItsTrue("username-first sign-in succeeds", outcome.NamedSucceeded);
            because.ItsTrue("username-first sign-in begins with the handle", outcome.NamedHandle == "ada");
            because.ItsTrue("completion gets the challenge with its ceremony id", outcome.CeremonyId == "ceremony-1");
            because.ItsTrue("the authenticator receives the backend's options unchanged", outcome.AssertedWith == "{\"challenge\":\"auth\"}");
            because.ItsTrue("the backend receives the assertion unchanged", outcome.CompletedWith == "{\"id\":\"asserted\"}");
            because.ItsTrue("the signed-in handle comes from the backend", outcome.SignedInHandle == "ada");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void TreatCancellationAsCancellation()
    {
        After.Setup(reg => reg.Set(new PasskeyServiceFixture()))
        .When<PasskeyServiceFixture>("has the user dismiss the prompt, then cancels the token up front", (fixture, reg) =>
        {
            fixture.Authenticator.AssertFailure = new PasskeyException(PasskeyErrorCategory.UserCancelled);
            PasskeyResult<PasskeySignIn> dismissed = Wait(fixture.Service().AuthenticateAsync());
            string dismissedCalls = fixture.Log.Joined;
            fixture.Log.Calls.Clear();

            fixture.Authenticator.AssertFailure = new OperationCanceledException();
            PasskeyResult<PasskeySignIn> thrown = Wait(fixture.Service().AuthenticateAsync());
            fixture.Log.Calls.Clear();

            using CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();
            PasskeyResult<PasskeySignIn> preCancelled = Wait(fixture.Service().AuthenticateAsync(source.Token));
            return new CancellationOutcome(
                dismissed.Succeeded, dismissed.WasCancelled, dismissed.Error?.Category, dismissedCalls,
                thrown.WasCancelled,
                preCancelled.WasCancelled, fixture.Log.Calls.Count);
        })
        .TheTest
        .ShouldPass<CancellationOutcome>((because, outcome) =>
        {
            because.ItsTrue("a dismissed prompt is not a success", !outcome.DismissedSucceeded);
            because.ItsTrue("a dismissed prompt is reported as cancelled", outcome.DismissedCancelled);
            because.ItsTrue("the category is UserCancelled", outcome.DismissedCategory == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("nothing is sent to the backend after a dismissal", outcome.DismissedCalls == "backend.beginAuthentication,authenticator.assert");
            because.ItsTrue("OperationCanceledException from the platform is reported as cancelled", outcome.ThrownCancelled);
            because.ItsTrue("a pre-cancelled token is reported as cancelled", outcome.PreCancelled);
            because.ItsTrue("a pre-cancelled token touches neither collaborator", outcome.PreCancelledCalls == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NormalizeCollaboratorFailures()
    {
        After.Setup(reg => reg.Set(new PasskeyServiceFixture()))
        .When<PasskeyServiceFixture>("fails at each step of a ceremony", (unused, reg) =>
        {
            return new FailureOutcome(
                SignInFailure(f => f.Backend.Failures["backend.beginAuthentication"] = new HttpRequestException("offline")),
                SignInFailure(f => f.Backend.Failures["backend.completeAuthentication"] = new PasskeyException(PasskeyErrorCategory.ServerVerificationFailed)),
                SignInFailure(f => f.Backend.Failures["backend.completeAuthentication"] = new PasskeyException(PasskeyErrorCategory.ExpiredChallenge)),
                SignInFailure(f => f.Authenticator.AssertFailure = new PasskeyException(PasskeyErrorCategory.NoCredentials)),
                SignInFailure(f => f.Authenticator.AssertFailure = new InvalidOperationException("native crash detail")),
                SignInFailure(f => f.Backend.ReturnNullOptions = true),
                SignInFailure(f => f.Backend.SignIn = null),
                SignInFailure(f => f.Backend.SignIn = new PasskeySignIn { Handle = " " }),
                RegistrationFailure(f => f.Authenticator.CreateFailure = new PasskeyException(PasskeyErrorCategory.PlatformFailure)),
                RegistrationFailure(f => f.Backend.ReturnNullOptions = true),
                Wait(new PasskeyServiceFixture().Service().RegisterPasskeyAsync(new PasskeyRegistrationRequest { Handle = "" })).Error?.Category,
                Wait(new PasskeyServiceFixture().Service().RegisterPasskeyAsync(null!)).Error?.Category,
                Wait(new PasskeyServiceFixture().Service().AuthenticateWithUsernameAsync(" ")).Error?.Category,
                SignInMessage(f => f.Authenticator.AssertFailure = new InvalidOperationException("native crash detail")));
        })
        .TheTest
        .ShouldPass<FailureOutcome>((because, outcome) =>
        {
            because.ItsTrue("an unreachable backend is a network failure", outcome.Offline == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("a backend refusal is a verification failure", outcome.Refused == PasskeyErrorCategory.ServerVerificationFailed);
            because.ItsTrue("an expired challenge keeps its category", outcome.Expired == PasskeyErrorCategory.ExpiredChallenge);
            because.ItsTrue("no credentials keeps its category", outcome.NoCredentials == PasskeyErrorCategory.NoCredentials);
            because.ItsTrue("an unexpected platform exception is unknown", outcome.NativeCrash == PasskeyErrorCategory.Unknown);
            because.ItsTrue("missing options from the backend is an invalid server response", outcome.NullOptions == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a completion without an identity is an invalid server response", outcome.NullSignIn == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a completion with a blank handle is an invalid server response", outcome.BlankSignIn == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a platform failure during creation keeps its category", outcome.CreateFailed == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("missing creation options is an invalid server response", outcome.NullCreationOptions == PasskeyErrorCategory.InvalidServerResponse);
            because.ItsTrue("a registration without a handle is invalid options", outcome.BlankHandle == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("a null registration request is invalid options", outcome.NullRequest == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("username-first sign-in without a handle is invalid options", outcome.BlankUsername == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("the platform exception's text never reaches the result", outcome.NativeCrashMessage is not null && !outcome.NativeCrashMessage.Contains("native crash detail"));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseDisabledFeaturesAndUnsupportedDevices()
    {
        After.Setup(reg => reg.Set(new PasskeyServiceFixture()))
        .When<PasskeyServiceFixture>("runs each operation with its feature off, then on an unsupported device", (unused, reg) =>
        {
            PasskeyServiceFixture off = new PasskeyServiceFixture();
            off.Configuration.Features = new PasskeyFeatures { EnableRegistration = false, EnableAuthentication = false, EnableManagement = false, EnableUsernameLessAuthentication = false };
            PasskeyService disabled = off.Service();
            List<PasskeyErrorCategory?> categories = new List<PasskeyErrorCategory?>
            {
                Wait(disabled.RegisterPasskeyAsync(new PasskeyRegistrationRequest { Handle = "ada" })).Error?.Category,
                Wait(disabled.AddPasskeyAsync()).Error?.Category,
                Wait(disabled.AuthenticateAsync()).Error?.Category,
                Wait(disabled.AuthenticateWithUsernameAsync("ada")).Error?.Category,
                Wait(disabled.GetRegisteredPasskeysAsync()).Error?.Category,
                Wait(disabled.DeletePasskeyAsync("id")).Error?.Category
            };

            PasskeyServiceFixture usernameOnly = new PasskeyServiceFixture();
            usernameOnly.Configuration.Features.EnableUsernameLessAuthentication = false;
            PasskeyErrorCategory? usernameLessOff = Wait(usernameOnly.Service().AuthenticateAsync()).Error?.Category;
            bool usernameFirstStillWorks = Wait(usernameOnly.Service().AuthenticateWithUsernameAsync("ada")).Succeeded;

            PasskeyServiceFixture noLock = new PasskeyServiceFixture();
            noLock.Authenticator.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.DeviceSecurityNotConfigured };
            PasskeyErrorCategory? noLockCategory = Wait(noLock.Service().AuthenticateAsync()).Error?.Category;

            PasskeyServiceFixture probeFails = new PasskeyServiceFixture();
            probeFails.Authenticator.CapabilitiesFailure = new InvalidOperationException("probe");
            PasskeyErrorCategory? probeCategory = Wait(probeFails.Service().RegisterPasskeyAsync(new PasskeyRegistrationRequest { Handle = "ada" })).Error?.Category;

            PasskeyService unsupported = new PasskeyService(noLock.Backend, new UnsupportedPasskeyAuthenticator(), noLock.Configuration, new PasskeyErrorNormalizer(noLock.Configuration.Messages));
            return new GateOutcome(
                categories.All(c => c == PasskeyErrorCategory.FeatureDisabled),
                off.Log.Calls.Count,
                usernameLessOff,
                usernameFirstStillWorks,
                noLockCategory,
                noLock.Log.Calls.Count,
                probeCategory,
                Wait(probeFails.Service().IsSupportedAsync()),
                Wait(unsupported.AuthenticateAsync()).Error?.Category,
                Wait(unsupported.IsSupportedAsync()),
                Wait(new PasskeyServiceFixture().Service().IsSupportedAsync()));
        })
        .TheTest
        .ShouldPass<GateOutcome>((because, outcome) =>
        {
            because.ItsTrue("every switched-off operation reports FeatureDisabled", outcome.AllDisabled);
            because.ItsTrue("a switched-off operation touches neither collaborator", outcome.DisabledCalls == 0);
            because.ItsTrue("username-less sign-in can be off on its own", outcome.UsernameLessOff == PasskeyErrorCategory.FeatureDisabled);
            because.ItsTrue("username-first sign-in still works then", outcome.UsernameFirstStillWorks);
            because.ItsTrue("a device without a screen lock reports that", outcome.NoLock == PasskeyErrorCategory.DeviceSecurityNotConfigured);
            because.ItsTrue("an unsupported device never reaches the backend", outcome.NoLockCalls == 0);
            because.ItsTrue("a failing capability probe is treated as unsupported", outcome.ProbeFailed == PasskeyErrorCategory.UnsupportedOperatingSystem);
            because.ItsTrue("a failing capability probe is not supported", !outcome.ProbeFailedSupported);
            because.ItsTrue("the default authenticator fails gracefully", outcome.DefaultAuthenticator == PasskeyErrorCategory.UnsupportedOperatingSystem);
            because.ItsTrue("the default authenticator is not supported", !outcome.DefaultAuthenticatorSupported);
            because.ItsTrue("an available authenticator is supported", outcome.AvailableSupported);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ManageRegisteredPasskeys()
    {
        After.Setup(reg => reg.Set(new PasskeyServiceFixture()))
        .When<PasskeyServiceFixture>("lists, adds and deletes passkeys and reads status", (fixture, reg) =>
        {
            fixture.Backend.Credentials = new List<PasskeyCredential>
            {
                new PasskeyCredential { CredentialId = "one", Name = "Phone", CreatedUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PasskeyCredential { CredentialId = "two" }
            };
            PasskeyService service = fixture.Service();
            PasskeyResult<IReadOnlyList<PasskeyCredential>> listing = Wait(service.GetRegisteredPasskeysAsync());
            PasskeyStatus status = Wait(service.GetPasskeyStatusAsync());
            fixture.Log.Calls.Clear();
            PasskeyResult added = Wait(service.AddPasskeyAsync());
            string addCalls = fixture.Log.Joined;
            PasskeyResult deleted = Wait(service.DeletePasskeyAsync("two"));
            PasskeyResult blankDelete = Wait(service.DeletePasskeyAsync(""));

            fixture.Backend.Failures["backend.getPasskeys"] = new HttpRequestException("offline");
            PasskeyStatus offlineStatus = Wait(service.GetPasskeyStatusAsync());
            PasskeyResult<IReadOnlyList<PasskeyCredential>> offlineListing = Wait(service.GetRegisteredPasskeysAsync());

            PasskeyServiceFixture unsupported = new PasskeyServiceFixture();
            unsupported.Authenticator.Capabilities = PasskeyCapabilities.Unsupported("Test");
            unsupported.Backend.Credentials = fixture.Backend.Credentials;
            PasskeyResult<IReadOnlyList<PasskeyCredential>> unsupportedListing = Wait(unsupported.Service().GetRegisteredPasskeysAsync());

            return new ManagementOutcome(
                listing.Succeeded, listing.Value?.Count, listing.Value?[1].Name, listing.Value?[1].CreatedUtc,
                status.RegisteredPasskeyCount, status.HasRegisteredPasskeys, status.Capabilities.IsSupported,
                added.Succeeded, addCalls, fixture.Backend.CompletedRegistrationJson,
                deleted.Succeeded, fixture.Backend.DeletedCredentialId, blankDelete.Error?.Category,
                offlineStatus.RegisteredPasskeyCount, offlineStatus.HasRegisteredPasskeys, offlineListing.Error?.Category,
                unsupportedListing.Succeeded);
        })
        .TheTest
        .ShouldPass<ManagementOutcome>((because, outcome) =>
        {
            because.ItsTrue("listing succeeds", outcome.Listed);
            because.ItsTrue("both passkeys are listed", outcome.Count == 2);
            because.ItsTrue("a missing name stays null", outcome.SecondName is null);
            because.ItsTrue("a missing creation date stays null", outcome.SecondCreated is null);
            because.ItsTrue("status carries the count", outcome.StatusCount == 2);
            because.ItsTrue("status says passkeys exist", outcome.StatusHasPasskeys);
            because.ItsTrue("status carries capabilities", outcome.StatusSupported);
            because.ItsTrue("adding a passkey succeeds", outcome.Added);
            because.ItsTrue("add runs begin, native create, complete in order", outcome.AddCalls == "backend.beginAdditional,authenticator.create,backend.completeAdditional");
            because.ItsTrue("the new credential is sent to the backend unchanged", outcome.AddedJson == "{\"id\":\"created\"}");
            because.ItsTrue("deleting succeeds", outcome.Deleted);
            because.ItsTrue("the backend is asked to delete that credential", outcome.DeletedId == "two");
            because.ItsTrue("deleting without an id is invalid options", outcome.BlankDelete == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("status leaves the count unknown when the backend is unreachable", outcome.OfflineCount is null);
            because.ItsTrue("status does not claim passkeys it could not count", !outcome.OfflineHasPasskeys);
            because.ItsTrue("an offline listing is a network failure", outcome.OfflineListing == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("listing does not need a platform authenticator", outcome.ListedOnUnsupportedDevice);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static PasskeyErrorCategory? SignInFailure(Action<PasskeyServiceFixture> arrange)
    {
        PasskeyServiceFixture fixture = new PasskeyServiceFixture();
        arrange(fixture);
        return Wait(fixture.Service().AuthenticateAsync()).Error?.Category;
    }

    private static string? SignInMessage(Action<PasskeyServiceFixture> arrange)
    {
        PasskeyServiceFixture fixture = new PasskeyServiceFixture();
        arrange(fixture);
        return Wait(fixture.Service().AuthenticateAsync()).Error?.Message;
    }

    private static PasskeyErrorCategory? RegistrationFailure(Action<PasskeyServiceFixture> arrange)
    {
        PasskeyServiceFixture fixture = new PasskeyServiceFixture();
        arrange(fixture);
        return Wait(fixture.Service().RegisterPasskeyAsync(new PasskeyRegistrationRequest { Handle = "ada", DisplayName = "Ada" })).Error?.Category;
    }

    private static TResult Wait<TResult>(Task<TResult> task)
    {
        return task.GetAwaiter().GetResult();
    }

    private sealed record RegistrationOutcome(bool Succeeded, string? Handle, string Calls, string? CreatedWith, string? CompletedWith, string? CompletedHandle, string? CompletedEmail, string? CompletedInvite);

    private sealed record SignInOutcome(bool DiscoverableSucceeded, string? DiscoverableHandle, string DiscoverableCalls, bool NamedSucceeded, string? NamedHandle, string? CeremonyId, string? AssertedWith, string? CompletedWith, string? SignedInHandle);

    private sealed record CancellationOutcome(bool DismissedSucceeded, bool DismissedCancelled, PasskeyErrorCategory? DismissedCategory, string DismissedCalls, bool ThrownCancelled, bool PreCancelled, int PreCancelledCalls);

    private sealed record FailureOutcome(PasskeyErrorCategory? Offline, PasskeyErrorCategory? Refused, PasskeyErrorCategory? Expired, PasskeyErrorCategory? NoCredentials, PasskeyErrorCategory? NativeCrash, PasskeyErrorCategory? NullOptions, PasskeyErrorCategory? NullSignIn, PasskeyErrorCategory? BlankSignIn, PasskeyErrorCategory? CreateFailed, PasskeyErrorCategory? NullCreationOptions, PasskeyErrorCategory? BlankHandle, PasskeyErrorCategory? NullRequest, PasskeyErrorCategory? BlankUsername, string? NativeCrashMessage);

    private sealed record GateOutcome(bool AllDisabled, int DisabledCalls, PasskeyErrorCategory? UsernameLessOff, bool UsernameFirstStillWorks, PasskeyErrorCategory? NoLock, int NoLockCalls, PasskeyErrorCategory? ProbeFailed, bool ProbeFailedSupported, PasskeyErrorCategory? DefaultAuthenticator, bool DefaultAuthenticatorSupported, bool AvailableSupported);

    private sealed record ManagementOutcome(bool Listed, int? Count, string? SecondName, DateTime? SecondCreated, int? StatusCount, bool StatusHasPasskeys, bool StatusSupported, bool Added, string AddCalls, string? AddedJson, bool Deleted, string? DeletedId, PasskeyErrorCategory? BlankDelete, int? OfflineCount, bool OfflineHasPasskeys, PasskeyErrorCategory? OfflineListing, bool ListedOnUnsupportedDevice);
}
