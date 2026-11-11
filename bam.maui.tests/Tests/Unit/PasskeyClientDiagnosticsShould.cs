using Bam.DependencyInjection;
using Bam.Logging;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyClient diagnostics Should", Selector = "pkdg")]
public class PasskeyClientDiagnosticsShould : UnitTestMenuContainer
{
    private const string Marker = "SECRET-MARKER-eyJjaGFsbGVuZ2Ui";

    public PasskeyClientDiagnosticsShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ReportCancellationOnlyWhenTheCallerOrTheUserCancelled()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("cancels from the backend, from the caller and from the authenticator", (unused, reg) =>
        {
            // The backend gives up on its own; the caller's token is live.
            PasskeyClientFixture backendAbort = new PasskeyClientFixture();
            backendAbort.Backend.Failures["backend.beginAuthentication"] = new TaskCanceledException();
            PasskeyResult<PasskeySignIn> aborted = Wait(backendAbort.Client().AuthenticateAsync(CancellationToken.None));

            PasskeyClientFixture completeAbort = new PasskeyClientFixture();
            completeAbort.Backend.Failures["backend.completeAuthentication"] = new OperationCanceledException();
            PasskeyResult<PasskeySignIn> abortedAtComplete = Wait(completeAbort.Client().AuthenticateAsync());

            PasskeyClientFixture listAbort = new PasskeyClientFixture();
            listAbort.Backend.Failures["backend.getPasskeys"] = new TaskCanceledException();
            PasskeyResult<IReadOnlyList<PasskeyCredential>> abortedListing = Wait(listAbort.Client().GetRegisteredPasskeysAsync());

            PasskeyClientFixture deleteAbort = new PasskeyClientFixture();
            deleteAbort.Backend.Failures["backend.deletePasskey"] = new TaskCanceledException();
            PasskeyResult abortedDelete = Wait(deleteAbort.Client().DeletePasskeyAsync("id"));

            // The caller cancels while the backend call is in flight.
            PasskeyClientFixture callerCancels = new PasskeyClientFixture();
            using CancellationTokenSource source = new CancellationTokenSource();
            callerCancels.Backend.BeforeCall = call => source.Cancel();
            callerCancels.Backend.Failures["backend.beginAuthentication"] = new OperationCanceledException();
            PasskeyResult<PasskeySignIn> callerCancelled = Wait(callerCancels.Client().AuthenticateAsync(source.Token));

            // The user dismisses the native prompt; the caller's token is live.
            PasskeyClientFixture dismissed = new PasskeyClientFixture();
            dismissed.Authenticator.AssertFailure = new OperationCanceledException();
            PasskeyResult<PasskeySignIn> userDismissed = Wait(dismissed.Client().AuthenticateAsync());

            return new CancellationSourceOutcome(
                aborted.Error?.Category, aborted.WasCancelled,
                abortedAtComplete.Error?.Category,
                abortedListing.Error?.Category,
                abortedDelete.Error?.Category,
                callerCancelled.Error?.Category,
                userDismissed.Error?.Category);
        })
        .TheTest
        .ShouldPass<CancellationSourceOutcome>((because, outcome) =>
        {
            because.ItsTrue("a backend cancellation with the caller's token live is a network failure", outcome.BackendAbort == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("and is not reported as cancelled", !outcome.BackendAbortWasCancelled);
            because.ItsTrue("the same holds at completion", outcome.BackendAbortAtComplete == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("the same holds for listing", outcome.ListingAbort == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("the same holds for deletion", outcome.DeleteAbort == PasskeyErrorCategory.NetworkFailure);
            because.ItsTrue("a cancellation after the caller cancelled is user cancelled", outcome.CallerCancelled == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("a cancellation from the authenticator is user cancelled", outcome.UserDismissed == PasskeyErrorCategory.UserCancelled);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NotMistakeACancelledProbeForAnUnsupportedDevice()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("cancels while the capability probe is running", (unused, reg) =>
        {
            PasskeyClientFixture fixture = new PasskeyClientFixture();
            using CancellationTokenSource source = new CancellationTokenSource();
            fixture.Authenticator.CapabilitiesProbe = token =>
            {
                source.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(fixture.Authenticator.Capabilities);
            };
            PasskeyClient client = fixture.Client();
            PasskeyResult<PasskeySignIn> ceremony = Wait(client.AuthenticateAsync(source.Token));

            // A probe that throws a cancellation nobody asked for is a failed probe, not a cancelled one.
            PasskeyClientFixture strayCancellation = new PasskeyClientFixture();
            strayCancellation.Authenticator.CapabilitiesFailure = new OperationCanceledException();
            bool strayIsSupported = Wait(strayCancellation.Client().IsSupportedAsync());

            return new ProbeOutcome(
                ceremony.Error?.Category,
                ceremony.WasCancelled,
                fixture.Log.Calls.Count,
                Cancels(() => Wait(client.GetCapabilitiesAsync(source.Token))),
                Cancels(() => Wait(client.IsSupportedAsync(source.Token))),
                Cancels(() => Wait(client.GetPasskeyStatusAsync(source.Token))),
                strayIsSupported);
        })
        .TheTest
        .ShouldPass<ProbeOutcome>((because, outcome) =>
        {
            because.ItsTrue("a ceremony cancelled during the probe is user cancelled, not unsupported", outcome.CeremonyCategory == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("and says it was cancelled", outcome.CeremonyWasCancelled);
            because.ItsTrue("and reaches neither the backend nor a ceremony", outcome.CollaboratorCalls == 0);
            because.ItsTrue("GetCapabilitiesAsync rethrows the caller's cancellation", outcome.CapabilitiesThrew);
            because.ItsTrue("IsSupportedAsync rethrows the caller's cancellation", outcome.IsSupportedThrew);
            because.ItsTrue("GetPasskeyStatusAsync rethrows the caller's cancellation", outcome.StatusThrew);
            because.ItsTrue("a cancellation the caller did not ask for is a failed probe: not supported, no throw", !outcome.StrayCancellationSupported);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void LogCategoryAndTypeNamesButNeverExceptionText()
    {
        After.Setup(reg => reg.Set(new CapturingLogger()))
        .When<CapturingLogger>("fails ceremonies with exceptions that carry marker text everywhere", (logger, reg) =>
        {
            SignInWith(logger, f => f.Authenticator.AssertFailure = new InvalidOperationException(Marker));
            SignInWith(logger, f => f.Backend.Failures["backend.completeAuthentication"] = new PasskeyException(PasskeyErrorCategory.ServerVerificationFailed, Marker, new FormatException(Marker)));
            SignInWith(logger, f => f.Backend.Failures["backend.beginAuthentication"] = new HttpRequestException(Marker, new IOException(Marker)));
            SignInWith(logger, f => f.Authenticator.CapabilitiesFailure = new InvalidOperationException(Marker));
            int failureEvents = logger.Events.Count;
            SignInWith(logger, f => f.Authenticator.AssertFailure = new PasskeyException(PasskeyErrorCategory.UserCancelled, Marker));

            IReadOnlyList<LogEvent> events = logger.Events;
            string text = logger.AllText;
            return new LogOutcome(
                failureEvents,
                events.Take(failureEvents).All(e => e.Severity == LogEventType.Warning),
                events.Last().Severity,
                text.Contains(Marker),
                text.Contains("username-less authentication"),
                text.Contains(nameof(PasskeyErrorCategory.ServerVerificationFailed)),
                text.Contains(nameof(InvalidOperationException)),
                text.Contains(nameof(FormatException)),
                text.Contains(nameof(PasskeyErrorCategory.NetworkFailure)),
                text.Contains("capability probe"), string.Join(",", events.Select(e => e.Severity + ":" + e.MessageSignature)));
        })
        .TheTest
        .ShouldPass<LogOutcome>((because, outcome) =>
        {
            because.ItsTrue("each failure is logged (the failed probe logs twice: the probe, then the refused ceremony is not an exception)", outcome.FailureEvents == 4);
            because.ItsTrue("failures are logged as warnings", outcome.FailuresAreWarnings);
            because.ItsTrue("a cancellation is logged as information: " + outcome.Entries, outcome.CancellationSeverity == LogEventType.Information);
            because.ItsTrue("no exception message, diagnostic message or inner message is logged", !outcome.MarkerLogged);
            because.ItsTrue("the operation is named", outcome.OperationLogged);
            because.ItsTrue("the category is named", outcome.CategoryLogged);
            because.ItsTrue("the exception type is named", outcome.TypeLogged);
            because.ItsTrue("the inner exception type is named", outcome.InnerTypeLogged);
            because.ItsTrue("a transport failure is logged under its category", outcome.NetworkLogged);
            because.ItsTrue("a failed capability probe is logged", outcome.ProbeLogged);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SurviveABrokenLoggerAndUseTheRegistryLogger()
    {
        After.Setup(reg => reg.Set(new CapturingLogger()))
        .When<CapturingLogger>("fails a ceremony with a logger that throws, then with a logger bound on the registry", (logger, reg) =>
        {
            PasskeyClientFixture broken = new PasskeyClientFixture();
            broken.Authenticator.AssertFailure = new InvalidOperationException("boom");
            PasskeyClient withBrokenLogger = new PasskeyClient(broken.Backend, broken.Authenticator, broken.Configuration, new PasskeyErrorNormalizer(broken.Configuration.Messages), new ThrowingLogger());
            PasskeyResult<PasskeySignIn> result = Wait(withBrokenLogger.AuthenticateAsync());

            PasskeyClientFixture bound = new PasskeyClientFixture();
            bound.Authenticator.AssertFailure = new InvalidOperationException(Marker);
            ServiceRegistry registry = new ServiceRegistry();
            registry.For<IPasskeyBackend>().Use(bound.Backend);
            registry.For<IPasskeyAuthenticator>().Use(bound.Authenticator);
            registry.For<ILogger>().Use(logger);
            registry.AddPasskeyClient(bound.Configuration);
            Wait(registry.Get<IPasskeyClient>().AuthenticateAsync());

            return new LoggerWiringOutcome(result.Error?.Category, logger.Events.Count, logger.AllText.Contains(Marker));
        })
        .TheTest
        .ShouldPass<LoggerWiringOutcome>((because, outcome) =>
        {
            because.ItsTrue("a logger that throws does not replace the result", outcome.BrokenLoggerCategory == PasskeyErrorCategory.Unknown);
            because.ItsTrue("a logger bound on the registry receives the failure: " + outcome.RegistryLoggerEvents, outcome.RegistryLoggerEvents == 1);
            because.ItsTrue("and still gets no exception text", !outcome.MarkerLogged);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static void SignInWith(ILogger logger, Action<PasskeyClientFixture> arrange)
    {
        PasskeyClientFixture fixture = new PasskeyClientFixture();
        arrange(fixture);
        PasskeyClient client = new PasskeyClient(fixture.Backend, fixture.Authenticator, fixture.Configuration, new PasskeyErrorNormalizer(fixture.Configuration.Messages), logger);
        Wait(client.AuthenticateAsync());
    }

    private static bool Cancels(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static TResult Wait<TResult>(Task<TResult> task)
    {
        return task.GetAwaiter().GetResult();
    }

    private sealed record CancellationSourceOutcome(PasskeyErrorCategory? BackendAbort, bool BackendAbortWasCancelled, PasskeyErrorCategory? BackendAbortAtComplete, PasskeyErrorCategory? ListingAbort, PasskeyErrorCategory? DeleteAbort, PasskeyErrorCategory? CallerCancelled, PasskeyErrorCategory? UserDismissed);

    private sealed record ProbeOutcome(PasskeyErrorCategory? CeremonyCategory, bool CeremonyWasCancelled, int CollaboratorCalls, bool CapabilitiesThrew, bool IsSupportedThrew, bool StatusThrew, bool StrayCancellationSupported);

    private sealed record LogOutcome(int FailureEvents, bool FailuresAreWarnings, LogEventType CancellationSeverity, bool MarkerLogged, bool OperationLogged, bool CategoryLogged, bool TypeLogged, bool InnerTypeLogged, bool NetworkLogged, bool ProbeLogged, string Entries);

    private sealed record LoggerWiringOutcome(PasskeyErrorCategory? BrokenLoggerCategory, int RegistryLoggerEvents, bool MarkerLogged);
}
