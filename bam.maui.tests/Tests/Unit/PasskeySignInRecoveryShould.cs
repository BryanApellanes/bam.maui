using System.Globalization;
using System.Net;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

/// <summary>A waiting key-set recovery reported at sign-in (bam.maui#20, against bam.useraccounts#27): read from the answer and shown before the app takes over.</summary>
[UnitTestMenu("Passkey Sign In Recovery Should", Selector = "pksr")]
public class PasskeySignInRecoveryShould : UnitTestMenuContainer
{
    private const string Credential = "{\"id\":\"BAUG\",\"rawId\":\"BAUG\",\"type\":\"public-key\",\"response\":{\"clientDataJSON\":\"e30\"}}";
    private static readonly DateTime Effective = new DateTime(2026, 11, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PasskeyPendingRecovery Pending = new PasskeyPendingRecovery { RecoveryId = "cmVj", RemovedCredentialId = "laptop", HeirCredentialId = "phone", EffectiveUtc = Effective };

    public PasskeySignInRecoveryShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ReadAWaitingRecoveryFromTheSignIn()
    {
        After.Setup(reg => reg.Set(new List<string>()))
        .When<List<string>>("completes sign-ins that report a waiting recovery, none, a null one and a malformed one", (wrong, reg) =>
        {
            PasskeySignIn? waiting = SignIn("{\"handle\":\"ada\",\"nextUrl\":\"/account\",\"pendingRecovery\":{\"id\":\"cmVj\",\"removes\":\"AQID\",\"heir\":\"BAUG\",\"effectiveUtc\":\"2026-11-29T12:00:00Z\"}}", out PasskeyErrorCategory? waitingError);
            PasskeySignIn? none = SignIn("{\"handle\":\"ada\"}", out PasskeyErrorCategory? noneError);
            PasskeySignIn? nulled = SignIn("{\"handle\":\"ada\",\"pendingRecovery\":null}", out PasskeyErrorCategory? nulledError);
            SignIn("{\"handle\":\"ada\",\"pendingRecovery\":{\"id\":\"a/b\",\"heir\":\"BAUG\",\"effectiveUtc\":\"2026-11-29T12:00:00Z\"}}", out PasskeyErrorCategory? unsafeId);
            SignIn("{\"handle\":\"ada\",\"pendingRecovery\":{\"id\":\"cmVj\",\"heir\":\"BAUG\"}}", out PasskeyErrorCategory? noDate);
            PasskeyPendingRecovery? read = waiting?.PendingRecovery;
            return new ReadOutcome(read?.RecoveryId + "|" + read?.RemovedCredentialId + "|" + read?.HeirCredentialId + "|" + (read?.EffectiveUtc == Effective) + "|" + waiting?.NextUrl,
                waitingError, none?.PendingRecovery is null && noneError is null, nulled?.PendingRecovery is null && nulledError is null, unsafeId, noDate);
        })
        .TheTest
        .ShouldPass<ReadOutcome>((because, outcome) =>
        {
            because.ItsTrue("a waiting recovery comes with the sign-in: " + outcome.Waiting, outcome.Waiting == "cmVj|AQID|BAUG|True|/account" && outcome.WaitingError is null);
            because.ItsTrue("an absent one is none", outcome.NoneIsNull);
            because.ItsTrue("and so is a null one", outcome.NullIsNull);
            because.ItsTrue("a malformed one fails the answer rather than being dropped", outcome.UnsafeId == PasskeyErrorCategory.InvalidServerResponse && outcome.NoDate == PasskeyErrorCategory.InvalidServerResponse);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void HoldTheSignInUntilTheRecoveryIsSeen()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs in with a recovery waiting and continues; signs in again and cancels it; signs in with nothing waiting", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(new PasskeySignIn { Handle = "ada", PendingRecovery = Pending }));
            Wait(screen.SignInCommand.ExecuteAsync());
            HeldShot held = Held(screen, navigator);
            Wait(screen.ContinueSignedInCommand.ExecuteAsync());
            string continued = navigator.Joined + "|" + navigator.SignedIn?.Handle + "|" + screen.ShowsPendingRecovery + "|" + screen.IsReady;

            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(new PasskeySignIn { Handle = "ada", PendingRecovery = Pending }));
            Wait(screen.SignInCommand.ExecuteAsync());
            Wait(screen.CancelRecoveryCommand.ExecuteAsync());
            string cancelled = client.CancelledRecoveryId + "|" + navigator.Joined + "|" + screen.ShowsPendingRecovery;

            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(new PasskeySignIn { Handle = "ada" }));
            Wait(screen.SignInCommand.ExecuteAsync());
            return new HoldOutcome(held, continued, cancelled, navigator.Joined, string.Join(",", client.Calls));
        })
        .TheTest
        .ShouldPass<HoldOutcome>((because, outcome) =>
        {
            string expected = new PasskeyText().SignInPendingRecoveryMessage.Replace("{0}", Effective.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            because.ItsTrue("a sign-in with a recovery waiting stops on the notice, not the app: " + outcome.Held, outcome.Held.Shows && !outcome.Held.SigningIn && outcome.Held.Navigated == "");
            because.ItsTrue("which says when the key moves: " + outcome.Held.Text, outcome.Held.Text == expected);
            because.ItsTrue("and offers cancel and continue", outcome.Held.CanCancel && outcome.Held.CanContinue);
            because.ItsTrue("continue hands the sign-in to the app and clears the notice: " + outcome.Continued, outcome.Continued == "signedIn|ada|False|True");
            because.ItsTrue("cancel names the recovery, then hands over: " + outcome.Cancelled, outcome.Cancelled == "cmVj|signedIn,signedIn|False");
            because.ItsTrue("a sign-in with nothing waiting goes straight to the app: " + outcome.Navigated, outcome.Navigated == "signedIn,signedIn,signedIn");
            because.ItsTrue("authenticate, authenticate, cancel, authenticate: " + outcome.Calls, outcome.Calls == "authenticate,authenticate,cancelRecovery,authenticate");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void HandleARefusedOrUnavailableCancelAtSignIn()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("cancels a recovery that's already gone and goes back; cancels while offline; then signs in on an app that doesn't manage passkeys", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(new PasskeySignIn { Handle = "ada", PendingRecovery = Pending }));
            Wait(screen.SignInCommand.ExecuteAsync());
            client.CancelRecoveryResult = PasskeyResult.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "NotFound"));
            Wait(screen.CancelRecoveryCommand.ExecuteAsync());
            string gone = screen.HasFailed + "|" + screen.ErrorMessage + "|" + screen.OffersBack;
            Wait(screen.BackCommand.ExecuteAsync());
            string goneBack = screen.ShowsPendingRecovery + "|" + navigator.Joined;

            client.CancelRecoveryResult = PasskeyResult.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.NetworkFailure));
            Wait(screen.CancelRecoveryCommand.ExecuteAsync());
            string offline = screen.HasFailed + "|" + screen.OffersTryAgain;
            client.CancelRecoveryResult = PasskeyResult.Success();
            Wait(screen.TryAgainCommand.ExecuteAsync());
            string retried = navigator.Joined + "|" + screen.ShowsPendingRecovery;

            PasskeyConfiguration noManagement = PasskeyClientFixture.ValidConfiguration();
            noManagement.Features.EnableManagement = false;
            FakePasskeyNavigator other = new FakePasskeyNavigator();
            PasskeySignInViewModel unmanaged = new PasskeySignInViewModel(client, noManagement, other);
            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(new PasskeySignIn { Handle = "ada", PendingRecovery = Pending }));
            Wait(unmanaged.SignInCommand.ExecuteAsync());
            HeldShot elsewhere = Held(unmanaged, other);
            return new CancelOutcome(gone, goneBack, offline, retried, elsewhere);
        })
        .TheTest
        .ShouldPass<CancelOutcome>((because, outcome) =>
        {
            PasskeyText text = new PasskeyText();
            because.ItsTrue("a recovery that's already gone says so: " + outcome.Gone, outcome.Gone == "True|" + text.RecoveryGoneMessage + "|True");
            because.ItsTrue("and Back returns to the notice, still signed in and not yet handed over: " + outcome.GoneBack, outcome.GoneBack == "True|");
            because.ItsTrue("an offline cancel offers Try again: " + outcome.Offline, outcome.Offline == "True|True");
            because.ItsTrue("which, once it works, hands over: " + outcome.Retried, outcome.Retried == "signedIn|False");
            because.ItsTrue("an app that doesn't manage passkeys shows the notice without Cancel: " + outcome.Elsewhere, outcome.Elsewhere.Shows && !outcome.Elsewhere.CanCancel && outcome.Elsewhere.CanContinue);
            because.ItsTrue("and says where to cancel it: " + outcome.Elsewhere.Text, outcome.Elsewhere.Text.EndsWith(" " + text.SignInPendingRecoveryElsewhereMessage, StringComparison.Ordinal));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void KeepTheHeirsNameOutOfTheTemplateAndRateLimitCancel()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("shows a waiting recovery to a passkey named {1}, one to a passkey not in the list, and has cancel rate-limited", (client, reg) =>
        {
            PasskeyCredential braces = new PasskeyCredential { CredentialId = "phone", Name = "{1} {0}", IsCurrent = true };
            PasskeyCredential laptop = new PasskeyCredential { CredentialId = "laptop", Name = "Laptop" };
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(braces, laptop));
            client.PendingRecovery = new PasskeyPendingRecovery { RecoveryId = "cmVj", HeirCredentialId = "phone", EffectiveUtc = Effective };
            PasskeyManagementViewModel screen = new PasskeyManagementViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            Wait(screen.LoadAsync());
            string named = screen.PendingRecoveryText;

            client.PendingRecovery = new PasskeyPendingRecovery { RecoveryId = "cmVj", HeirCredentialId = "tablet", EffectiveUtc = Effective };
            Wait(screen.LoadAsync());
            string unlisted = screen.PendingRecoveryText;

            FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(HttpStatusCode.TooManyRequests, "{\"error\":\"rateLimited\",\"message\":\"slow down\"}");
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
            PasskeyErrorCategory? limited = null;
            try
            {
                backend.CancelKeySetRecoveryAsync("cmVj").GetAwaiter().GetResult();
            }
            catch (PasskeyException exception)
            {
                limited = exception.Category;
            }
            return new TemplateOutcome(named, unlisted, limited);
        })
        .TheTest
        .ShouldPass<TemplateOutcome>((because, outcome) =>
        {
            PasskeyText text = new PasskeyText();
            string date = Effective.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            because.ItsTrue("a heir named with braces is shown as typed, never expanded: " + outcome.Named, outcome.Named == text.PendingRecoveryMessage.Replace("{1}", date).Replace("{0}", "{1} {0}"));
            because.ItsTrue("a heir not in the list doesn't look like an ordinary passkey: " + outcome.Unlisted, outcome.Unlisted == text.PendingRecoveryMessage.Replace("{1}", date).Replace("{0}", text.UnlistedHeir));
            because.ItsTrue("a rate-limited cancel is rate limited", outcome.Limited == PasskeyErrorCategory.RateLimited);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static PasskeySignIn? SignIn(string body, out PasskeyErrorCategory? error)
    {
        FakeAccountEndpoints fake = new FakeAccountEndpoints().Respond(HttpStatusCode.OK, body);
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(PasskeyClientFixture.ValidConfiguration(), null, fake);
        try
        {
            error = null;
            return backend.CompleteAuthenticationAsync(new PasskeyAuthenticationChallenge { Options = new WebAuthnRequestOptions("{}"), CeremonyId = "c-1" }, new WebAuthnAssertionResponse(Credential)).GetAwaiter().GetResult();
        }
        catch (PasskeyException exception)
        {
            error = exception.Category;
            return null;
        }
    }

    private static HeldShot Held(PasskeySignInViewModel screen, FakePasskeyNavigator navigator)
    {
        return new HeldShot(screen.ShowsPendingRecovery, screen.ShowsSigningIn, screen.PendingRecoveryText, screen.CancelRecoveryCommand.CanExecute(null), screen.ContinueSignedInCommand.CanExecute(null), navigator.Joined);
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record ReadOutcome(string Waiting, PasskeyErrorCategory? WaitingError, bool NoneIsNull, bool NullIsNull, PasskeyErrorCategory? UnsafeId, PasskeyErrorCategory? NoDate);

    private sealed record HeldShot(bool Shows, bool SigningIn, string Text, bool CanCancel, bool CanContinue, string Navigated);

    private sealed record HoldOutcome(HeldShot Held, string Continued, string Cancelled, string Navigated, string Calls);

    private sealed record CancelOutcome(string Gone, string GoneBack, string Offline, string Retried, HeldShot Elsewhere);

    private sealed record TemplateOutcome(string Named, string Unlisted, PasskeyErrorCategory? Limited);
}
