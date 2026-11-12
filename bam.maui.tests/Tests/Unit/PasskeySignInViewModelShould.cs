using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeySignInViewModel Should", Selector = "pksi")]
public class PasskeySignInViewModelShould : UnitTestMenuContainer
{
    public PasskeySignInViewModelShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ShowWhatTheSignInModeAsksFor()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("builds the screen in each mode, and in choice mode with username-less sign-in off", (client, reg) =>
        {
            PasskeySignInViewModel choice = Screen(client, c => { });
            PasskeySignInViewModel usernameLess = Screen(client, c => c.Experience.SignInMode = PasskeySignInMode.UsernameLess);
            PasskeySignInViewModel usernameFirst = Screen(client, c => c.Experience.SignInMode = PasskeySignInMode.UsernameFirst);
            PasskeySignInViewModel fallback = Screen(client, c => c.Features.EnableUsernameLessAuthentication = false);
            bool continueBlank = choice.ContinueCommand.CanExecute(null);
            choice.Handle = "   ";
            bool continueWhitespace = choice.ContinueCommand.CanExecute(null);
            choice.Handle = "ada";
            bool continueTyped = choice.ContinueCommand.CanExecute(null);
            choice.Handle = null!;
            return new ModesOutcome(
                Visible(choice), Visible(usernameLess), Visible(usernameFirst), fallback.Mode,
                usernameFirst.SignInCommand.CanExecute(null), choice.SignInCommand.CanExecute(null),
                continueBlank, continueWhitespace, continueTyped, choice.Handle,
                choice.Title, choice.State, choice.ShowsForm, choice.OffersRegistration, choice.HasAlternative);
        })
        .TheTest
        .ShouldPass<ModesOutcome>((because, outcome) =>
        {
            because.ItsTrue("choice mode shows the button, the field and the hint: " + outcome.Choice, outcome.Choice == "button,username,hint");
            because.ItsTrue("username-less mode shows only the button: " + outcome.UsernameLess, outcome.UsernameLess == "button");
            because.ItsTrue("username-first mode shows only the field: " + outcome.UsernameFirst, outcome.UsernameFirst == "username");
            because.ItsTrue("choice mode falls back to username-first when username-less sign-in is off", outcome.Fallback == PasskeySignInMode.UsernameFirst);
            because.ItsTrue("the passkey button can't run in username-first mode", !outcome.ButtonInUsernameFirst);
            because.ItsTrue("the passkey button can run in choice mode", outcome.ButtonInChoice);
            because.ItsTrue("continue needs a username", !outcome.ContinueBlank);
            because.ItsTrue("a whitespace username is not a username", !outcome.ContinueWhitespace);
            because.ItsTrue("continue runs once a username is typed", outcome.ContinueTyped);
            because.ItsTrue("a null username reads as empty", outcome.NullHandle == string.Empty);
            because.ItsTrue("the title carries the app name: " + outcome.Title, outcome.Title == "Sign in to Example");
            because.ItsTrue("the screen starts ready", outcome.State == PasskeyViewState.Ready);
            because.ItsTrue("the form shows when ready", outcome.ShowsForm);
            because.ItsTrue("registration is offered by default", outcome.OffersRegistration);
            because.ItsTrue("no alternative without a label", !outcome.HasAlternative);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NavigateOnlyAfterAVerifiedSignIn()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs in with the passkey button, then with a padded username", (client, reg) =>
        {
            FakePasskeyNavigator buttonNavigator = new FakePasskeyNavigator();
            PasskeySignInViewModel button = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), buttonNavigator);
            Wait(button.SignInCommand.ExecuteAsync());

            FakePasskeyNavigator usernameNavigator = new FakePasskeyNavigator();
            PasskeySignInViewModel username = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), usernameNavigator);
            username.Handle = "  ada  ";
            Wait(username.ContinueCommand.ExecuteAsync());
            return new SignedInOutcome(
                buttonNavigator.Joined, buttonNavigator.SignedIn?.Handle, buttonNavigator.SignedInFrom, button.State, button.HasSucceeded, button.ShowsForm,
                usernameNavigator.Joined, client.LastHandle, string.Join(",", client.Calls));
        })
        .TheTest
        .ShouldPass<SignedInOutcome>((because, outcome) =>
        {
            because.ItsTrue("the button sign-in navigates once: " + outcome.ButtonCalls, outcome.ButtonCalls == "signedIn");
            because.ItsTrue("the navigator gets the signed-in account", outcome.SignedInHandle == "ada");
            because.ItsTrue("the navigator is told it came from sign-in", outcome.SignedInFrom == PasskeyFlow.SignIn);
            because.ItsTrue("the screen shows success", outcome.State == PasskeyViewState.Succeeded && outcome.HasSucceeded);
            because.ItsTrue("the form is hidden after success", !outcome.ShowsForm);
            because.ItsTrue("the username sign-in navigates once: " + outcome.UsernameCalls, outcome.UsernameCalls == "signedIn");
            because.ItsTrue("the username is trimmed before it is sent: [" + outcome.SentHandle + "]", outcome.SentHandle == "ada");
            because.ItsTrue("each button calls its own ceremony: " + outcome.ClientCalls, outcome.ClientCalls == "authenticate,authenticateWithUsername");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void TreatACancelAsQuiet()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("dismisses the prompt, then cancels a running ceremony from the screen", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.UserCancelled));
            Wait(screen.SignInCommand.ExecuteAsync());
            CancelState dismissed = new CancelState(screen.State, screen.HasFailed, screen.HasNote, screen.Note, screen.Recovery is null);

            client.Gate = new TaskCompletionSource<bool>();
            bool cancelBefore = screen.CancelCommand.CanExecute(null);
            Task running = screen.SignInCommand.ExecuteAsync();
            PasskeyViewState whileRunning = screen.State;
            bool showsFormWhileRunning = screen.ShowsForm;
            bool cancelWhileRunning = screen.CancelCommand.CanExecute(null);
            bool signInWhileRunning = screen.SignInCommand.CanExecute(null);
            bool tryAgainWhileRunning = screen.TryAgainCommand.CanExecute(null);
            bool hasNoteWhileRunning = screen.HasNote;
            Wait(screen.CancelCommand.ExecuteAsync());
            Wait(running);
            CancelState cancelled = new CancelState(screen.State, screen.HasFailed, screen.HasNote, screen.Note, screen.Recovery is null);
            return new CancelOutcome(dismissed, cancelled, cancelBefore, whileRunning, showsFormWhileRunning, cancelWhileRunning, signInWhileRunning,
                tryAgainWhileRunning, hasNoteWhileRunning, screen.CancelCommand.CanExecute(null), screen.SignInCommand.CanExecute(null), navigator.Joined,
                screen.Text.Cancelled);
        })
        .TheTest
        .ShouldPass<CancelOutcome>((because, outcome) =>
        {
            because.ItsTrue("a dismissed prompt returns to the form", outcome.Dismissed.State == PasskeyViewState.Ready && !outcome.Dismissed.HasFailed);
            because.ItsTrue("a dismissed prompt shows a note, not an error", outcome.Dismissed.HasNote && outcome.Dismissed.NoRecovery);
            because.ItsTrue("the note is the cancelled text: " + outcome.Dismissed.Note, outcome.Dismissed.Note == outcome.CancelledText);
            because.ItsTrue("cancel can't run with nothing running", !outcome.CancelBefore);
            because.ItsTrue("the screen is working while the ceremony runs", outcome.WhileRunning == PasskeyViewState.Working);
            because.ItsTrue("the form stays while working", outcome.ShowsFormWhileRunning);
            because.ItsTrue("cancel can run while the ceremony runs", outcome.CancelWhileRunning);
            because.ItsTrue("sign-in can't start twice", !outcome.SignInWhileRunning);
            because.ItsTrue("try again can't run while working", !outcome.TryAgainWhileRunning);
            because.ItsTrue("the earlier note clears when a new ceremony starts", !outcome.HasNoteWhileRunning);
            because.ItsTrue("a cancelled call that throws also returns quietly to the form", outcome.Cancelled.State == PasskeyViewState.Ready && !outcome.Cancelled.HasFailed);
            because.ItsTrue("a thrown cancel shows the cancelled note too", outcome.Cancelled.Note == outcome.CancelledText && outcome.Cancelled.NoRecovery);
            because.ItsTrue("cancel can't run after the ceremony ends", !outcome.CancelAfter);
            because.ItsTrue("sign-in can run again", outcome.SignInAfter);
            because.ItsTrue("a cancel never navigates: " + outcome.NavigatorCalls, outcome.NavigatorCalls.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void OfferRecoveryAfterAFailure()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("fails with no credentials, tries again, and follows the create-account and other-way links", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            configuration.Experience.AlternativeSignInLabel = "Email me a link";
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, configuration, navigator);
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.NoCredentials));
            Wait(screen.SignInCommand.ExecuteAsync());
            FailedState failed = new FailedState(screen.State, screen.ErrorTitle, screen.ErrorMessage, screen.HasErrorHint, screen.OffersTryAgain,
                screen.OffersCreateAccountInstead, screen.OffersAnotherWay, screen.OffersSignInInstead, screen.ShowsForm);
            Wait(screen.GoToRegistrationCommand.ExecuteAsync());
            Wait(screen.UseAnotherWayCommand.ExecuteAsync());
            Wait(screen.TryAgainCommand.ExecuteAsync());
            PasskeyViewState afterRetry = screen.State;

            PasskeySignInViewModel dismissable = new PasskeySignInViewModel(client, configuration, new FakePasskeyNavigator());
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.ServerVerificationFailed));
            Wait(dismissable.SignInCommand.ExecuteAsync());
            dismissable.Dismiss();
            return new RecoveryOutcome(failed, navigator.Joined, string.Join(",", client.Calls), afterRetry, configuration.Text.SignInFailedTitle,
                dismissable.State, dismissable.ErrorTitle, dismissable.UseAnotherWayCommand.CanExecute(null), dismissable.AlternativeLabel);
        })
        .TheTest
        .ShouldPass<RecoveryOutcome>((because, outcome) =>
        {
            because.ItsTrue("the screen shows the failure", outcome.Failed.State == PasskeyViewState.Failed && !outcome.Failed.ShowsForm);
            because.ItsTrue("the heading is the sign-in failure heading: " + outcome.Failed.Title, outcome.Failed.Title == outcome.ExpectedTitle);
            because.ItsTrue("the message is the client's: " + outcome.Failed.Message, outcome.Failed.Message == "message for NoCredentials");
            because.ItsTrue("no hint for a missing passkey", !outcome.Failed.HasHint);
            because.ItsTrue("try again, create an account and the other way are offered", outcome.Failed.TryAgain && outcome.Failed.CreateAccount && outcome.Failed.AnotherWay);
            because.ItsTrue("sign-in-instead is not offered on the sign-in screen", !outcome.Failed.SignInInstead);
            because.ItsTrue("the links reach the navigator, and the retry signs in: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "registration,alternative,signedIn");
            because.ItsTrue("try again runs the same ceremony: " + outcome.ClientCalls, outcome.ClientCalls.StartsWith("authenticate,authenticate", StringComparison.Ordinal));
            because.ItsTrue("the retry succeeds", outcome.AfterRetry == PasskeyViewState.Succeeded);
            because.ItsTrue("dismissing an error returns to the form", outcome.DismissedState == PasskeyViewState.Ready && outcome.DismissedTitle.Length == 0);
            because.ItsTrue("the other way stays available on the form", outcome.AnotherWayOnForm);
            because.ItsTrue("the other way carries the app's label", outcome.AlternativeLabel == "Email me a link");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void KeepTheButtonsOffOnADeviceThatCantDoPasskeys()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("loads on a device without a credential provider, then again once one is set up", (client, reg) =>
        {
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            client.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.MissingCredentialProvider, Platform = "Test" };
            Wait(screen.LoadAsync());
            screen.Handle = "ada";
            UnsupportedState missing = new UnsupportedState(screen.State, screen.ErrorTitle, screen.ErrorMessage, screen.HasErrorHint, screen.OffersTryAgain,
                screen.SignInCommand.CanExecute(null), screen.ContinueCommand.CanExecute(null));
            Wait(screen.SignInCommand.ExecuteAsync());
            int callsAfterBlockedSignIn = client.Calls.Count(c => c.StartsWith("authenticate", StringComparison.Ordinal));

            client.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.Available, Platform = "Test" };
            Wait(screen.TryAgainCommand.ExecuteAsync());
            UnsupportedState setUp = new UnsupportedState(screen.State, screen.ErrorTitle, screen.ErrorMessage, screen.HasErrorHint, screen.OffersTryAgain,
                screen.SignInCommand.CanExecute(null), screen.ContinueCommand.CanExecute(null));

            PasskeySignInViewModel old = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            client.Capabilities = PasskeyCapabilities.Unsupported("Test");
            Wait(old.LoadAsync());
            return new UnsupportedOutcome(missing, setUp, callsAfterBlockedSignIn, client.Calls.Count(c => c == "capabilities"),
                old.ErrorTitle, old.OffersTryAgain, old.SignInCommand.CanExecute(null), screen.Text.DeviceSetupTitle, screen.Text.UnavailableTitle,
                screen.Configuration.Messages.MissingCredentialProvider);
        })
        .TheTest
        .ShouldPass<UnsupportedOutcome>((because, outcome) =>
        {
            because.ItsTrue("a device without a provider shows the setup screen: " + outcome.Missing.Title, outcome.Missing.State == PasskeyViewState.Failed && outcome.Missing.Title == outcome.SetupTitle);
            because.ItsTrue("with the configured message", outcome.Missing.Message == outcome.MissingMessage);
            because.ItsTrue("with the setup hint and try again", outcome.Missing.HasHint && outcome.Missing.TryAgain);
            because.ItsTrue("the sign-in buttons are off", !outcome.Missing.SignIn && !outcome.Missing.Continue);
            because.ItsTrue("a blocked button runs no ceremony", outcome.CeremoniesWhileBlocked == 0);
            because.ItsTrue("try again probes again and returns to the form", outcome.SetUp.State == PasskeyViewState.Ready && outcome.SetUp.Title.Length == 0);
            because.ItsTrue("the sign-in buttons come back on", outcome.SetUp.SignIn && outcome.SetUp.Continue);
            because.ItsTrue("each load probes the device once: " + outcome.Probes, outcome.Probes == 3);
            because.ItsTrue("an unsupported device shows the unavailable screen: " + outcome.OldTitle, outcome.OldTitle == outcome.UnavailableTitle);
            because.ItsTrue("without try again", !outcome.OldTryAgain);
            because.ItsTrue("an unsupported device keeps the button off", !outcome.OldSignIn);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static PasskeySignInViewModel Screen(IPasskeyClient client, Action<PasskeyConfiguration> configure)
    {
        PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
        configure(configuration);
        return new PasskeySignInViewModel(client, configuration, new FakePasskeyNavigator());
    }

    private static string Visible(PasskeySignInViewModel screen)
    {
        List<string> shown = new List<string>();
        if (screen.ShowsPasskeyButton)
        {
            shown.Add("button");
        }
        if (screen.ShowsUsername)
        {
            shown.Add("username");
        }
        if (screen.ShowsUsernameHint)
        {
            shown.Add("hint");
        }
        return string.Join(",", shown);
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record ModesOutcome(string Choice, string UsernameLess, string UsernameFirst, PasskeySignInMode Fallback, bool ButtonInUsernameFirst, bool ButtonInChoice,
        bool ContinueBlank, bool ContinueWhitespace, bool ContinueTyped, string NullHandle, string Title, PasskeyViewState State, bool ShowsForm, bool OffersRegistration, bool HasAlternative);

    private sealed record SignedInOutcome(string ButtonCalls, string? SignedInHandle, PasskeyFlow? SignedInFrom, PasskeyViewState State, bool HasSucceeded, bool ShowsForm,
        string UsernameCalls, string? SentHandle, string ClientCalls);

    private sealed record CancelState(PasskeyViewState State, bool HasFailed, bool HasNote, string? Note, bool NoRecovery);

    private sealed record CancelOutcome(CancelState Dismissed, CancelState Cancelled, bool CancelBefore, PasskeyViewState WhileRunning, bool ShowsFormWhileRunning,
        bool CancelWhileRunning, bool SignInWhileRunning, bool TryAgainWhileRunning, bool HasNoteWhileRunning, bool CancelAfter, bool SignInAfter, string NavigatorCalls, string CancelledText);

    private sealed record FailedState(PasskeyViewState State, string Title, string Message, bool HasHint, bool TryAgain, bool CreateAccount, bool AnotherWay, bool SignInInstead, bool ShowsForm);

    private sealed record RecoveryOutcome(FailedState Failed, string NavigatorCalls, string ClientCalls, PasskeyViewState AfterRetry, string ExpectedTitle,
        PasskeyViewState DismissedState, string DismissedTitle, bool AnotherWayOnForm, string AlternativeLabel);

    private sealed record UnsupportedState(PasskeyViewState State, string Title, string Message, bool HasHint, bool TryAgain, bool SignIn, bool Continue);

    private sealed record UnsupportedOutcome(UnsupportedState Missing, UnsupportedState SetUp, int CeremoniesWhileBlocked, int Probes, string OldTitle, bool OldTryAgain,
        bool OldSignIn, string SetupTitle, string UnavailableTitle, string MissingMessage);
}
