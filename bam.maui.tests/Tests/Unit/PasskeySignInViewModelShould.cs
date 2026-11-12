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
                choice.Title, choice.State, choice.IsReady, choice.OffersRegistration, choice.HasAlternative, choice.BackCommand.CanExecute(null));
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
            because.ItsTrue("back can't run on the form", !outcome.BackOnForm);
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
            PasskeyViewState whileNavigating = PasskeyViewState.Ready;
            buttonNavigator.WhileSignedIn = () => whileNavigating = button.State;
            Wait(button.SignInCommand.ExecuteAsync());

            FakePasskeyNavigator usernameNavigator = new FakePasskeyNavigator();
            PasskeySignInViewModel username = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), usernameNavigator);
            username.Handle = "  ada  ";
            Wait(username.ContinueCommand.ExecuteAsync());
            return new SignedInOutcome(
                buttonNavigator.Joined, buttonNavigator.SignedIn?.Handle, buttonNavigator.SignedInFrom, whileNavigating, button.State, button.SignInCommand.CanExecute(null),
                usernameNavigator.Joined, client.LastHandle, string.Join(",", client.Calls));
        })
        .TheTest
        .ShouldPass<SignedInOutcome>((because, outcome) =>
        {
            because.ItsTrue("the button sign-in navigates once: " + outcome.ButtonCalls, outcome.ButtonCalls == "signedIn");
            because.ItsTrue("the navigator gets the signed-in account", outcome.SignedInHandle == "ada");
            because.ItsTrue("the navigator is told it came from sign-in", outcome.SignedInFrom == PasskeyFlow.SignIn);
            because.ItsTrue("the screen is on success while the app navigates", outcome.WhileNavigating == PasskeyViewState.Succeeded);
            because.ItsTrue("then back on the form, for when the app shows it again", outcome.State == PasskeyViewState.Ready);
            because.ItsTrue("and sign-in can run again", outcome.SignInAgain);
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
            bool showsFormWhileRunning = screen.IsReady;
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
            because.ItsTrue("the progress view replaces the form while working", !outcome.ShowsFormWhileRunning);
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
        .When<ScriptedPasskeyClient>("fails with no credentials, follows the create-account and other-way links, then signs in from the form", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            configuration.Experience.AlternativeSignInLabel = "Email me a link";
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, configuration, navigator);
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.NoCredentials));
            Wait(screen.SignInCommand.ExecuteAsync());
            FailedState failed = new FailedState(screen.State, screen.ErrorTitle, screen.ErrorMessage, screen.HasErrorHint, screen.OffersTryAgain,
                screen.OffersCreateAccountInstead, screen.OffersAnotherWay, screen.OffersSignInInstead, screen.IsReady);
            Wait(screen.GoToRegistrationCommand.ExecuteAsync());
            PasskeyViewState afterLink = screen.State;
            Wait(screen.UseAnotherWayCommand.ExecuteAsync());
            bool tryAgainAfterLinks = screen.TryAgainCommand.CanExecute(null);
            Wait(screen.SignInCommand.ExecuteAsync());
            PasskeyViewState afterRetry = screen.State;

            PasskeySignInViewModel dismissable = new PasskeySignInViewModel(client, configuration, new FakePasskeyNavigator());
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.ServerVerificationFailed));
            Wait(dismissable.SignInCommand.ExecuteAsync());
            dismissable.Dismiss();
            return new RecoveryOutcome(failed, afterLink, tryAgainAfterLinks, navigator.Joined, string.Join(",", client.Calls), afterRetry, configuration.Text.SignInFailedTitle,
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
            because.ItsTrue("the links reach the navigator, and signing in again works: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "registration,alternative,signedIn");
            because.ItsTrue("a link that works clears the error: " + outcome.AfterLink, outcome.AfterLink == PasskeyViewState.Ready);
            because.ItsTrue("and its try again with it", !outcome.TryAgainAfterLinks);
            because.ItsTrue("signing in again runs the same ceremony: " + outcome.ClientCalls, outcome.ClientCalls.StartsWith("authenticate,authenticate", StringComparison.Ordinal));
            because.ItsTrue("the sign-in works and the screen is back on the form", outcome.AfterRetry == PasskeyViewState.Ready);
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
            bool missingBack = screen.OffersBack || screen.BackCommand.CanExecute(null);
            Wait(screen.BackCommand.ExecuteAsync());
            UnsupportedState afterBack = new UnsupportedState(screen.State, screen.ErrorTitle, screen.ErrorMessage, screen.HasErrorHint, screen.OffersTryAgain,
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
            bool oldBack = old.OffersBack || old.BackCommand.CanExecute(null);
            Wait(old.BackCommand.ExecuteAsync());
            return new UnsupportedOutcome(missing, missingBack, afterBack, setUp, oldBack, old.State, old.ErrorMessage, old.Configuration.Messages.UnsupportedOperatingSystem, callsAfterBlockedSignIn, client.Calls.Count(c => c == "capabilities"),
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
            because.ItsTrue("the setup screen offers no back: it replaces a form that can't be used", !outcome.MissingBack);
            because.ItsTrue("a back attempt leaves the reason and the re-check up", outcome.AfterBack == outcome.Missing);
            because.ItsTrue("a blocked button runs no ceremony", outcome.CeremoniesWhileBlocked == 0);
            because.ItsTrue("try again probes again and returns to the form", outcome.SetUp.State == PasskeyViewState.Ready && outcome.SetUp.Title.Length == 0);
            because.ItsTrue("the sign-in buttons come back on", outcome.SetUp.SignIn && outcome.SetUp.Continue);
            because.ItsTrue("each load probes the device once: " + outcome.Probes, outcome.Probes == 3);
            because.ItsTrue("an unsupported device shows the unavailable screen: " + outcome.OldTitle, outcome.OldTitle == outcome.UnavailableTitle);
            because.ItsTrue("without try again", !outcome.OldTryAgain);
            because.ItsTrue("an unsupported device keeps the button off", !outcome.OldSignIn);
            because.ItsTrue("and offers no back to a dead form", !outcome.OldBack);
            because.ItsTrue("so the reason stays on screen: " + outcome.OldMessage, outcome.OldState == PasskeyViewState.Failed && outcome.OldMessage == outcome.UnsupportedMessage);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ComeBackToTheDeviceCheckFromALaterError()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("needs setup, follows a link the app can't route, goes back, retries the link, then sets up", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator { AlternativeFailure = new InvalidOperationException("route not found") };
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            configuration.Experience.AlternativeSignInLabel = "Email me a link";
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, configuration, navigator);
            client.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.MissingCredentialProvider, Platform = "Test" };
            Wait(screen.LoadAsync());

            Wait(screen.UseAnotherWayCommand.ExecuteAsync());
            bool linkFailureOffersBack = screen.OffersBack && screen.BackCommand.CanExecute(null);
            Wait(screen.BackCommand.ExecuteAsync());
            CheckScreen afterBack = Check(screen);

            Wait(screen.UseAnotherWayCommand.ExecuteAsync());
            navigator.AlternativeFailure = null;
            Wait(screen.TryAgainCommand.ExecuteAsync());
            CheckScreen afterRetriedLink = Check(screen);

            client.Capabilities = new PasskeyCapabilities { Availability = PasskeyAvailability.Available, Platform = "Test" };
            Wait(screen.TryAgainCommand.ExecuteAsync());
            return new LaterErrorOutcome(linkFailureOffersBack, afterBack, afterRetriedLink, screen.State, screen.SignInCommand.CanExecute(null),
                configuration.Text.DeviceSetupTitle, configuration.Messages.MissingCredentialProvider, navigator.Calls.Count(c => c == "alternative"));
        })
        .TheTest
        .ShouldPass<LaterErrorOutcome>((because, outcome) =>
        {
            because.ItsTrue("the link's failure has its own back", outcome.LinkFailureOffersBack);
            because.ItsTrue("back returns to the device check, not the form: " + outcome.AfterBack, outcome.AfterBack.IsSetupScreen(outcome.SetupTitle, outcome.SetupMessage));
            because.ItsTrue("so does a retried link that works: " + outcome.AfterRetriedLink, outcome.AfterRetriedLink.IsSetupScreen(outcome.SetupTitle, outcome.SetupMessage));
            because.ItsTrue("the app is asked three times: " + outcome.Requests, outcome.Requests == 3);
            because.ItsTrue("its try again still checks the device, and a set-up device gets the form", outcome.Final == PasskeyViewState.Ready && outcome.SignInAfterSetup);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void LeaveANewerErrorAloneWhenAnOlderLinkFinishes()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("fails, starts a slow link, follows a second link that fails at once, then the slow link finishes", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator
            {
                AlternativeGate = new TaskCompletionSource(),
                RegistrationFailure = new InvalidOperationException("route not found")
            };
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            configuration.Experience.AlternativeSignInLabel = "Email me a link";
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, configuration, navigator);
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.NoCredentials));
            Wait(screen.SignInCommand.ExecuteAsync());
            string firstMessage = screen.ErrorMessage;

            Task slowLink = screen.UseAnotherWayCommand.ExecuteAsync();
            Wait(screen.GoToRegistrationCommand.ExecuteAsync());
            string newerMessage = screen.ErrorMessage;
            navigator.AlternativeGate.SetResult();
            Wait(slowLink);
            return new NewerErrorOutcome(firstMessage, newerMessage, screen.State, screen.ErrorMessage, screen.OffersBack,
                configuration.Messages.For(PasskeyErrorCategory.Unknown));
        })
        .TheTest
        .ShouldPass<NewerErrorOutcome>((because, outcome) =>
        {
            because.ItsTrue("the second link's failure replaces the first error: " + outcome.NewerMessage,
                outcome.NewerMessage == outcome.UnknownMessage && outcome.FirstMessage != outcome.UnknownMessage);
            because.ItsTrue("the older link finishing leaves the newer error up: " + outcome.Final,
                outcome.Final == PasskeyViewState.Failed && outcome.FinalMessage == outcome.UnknownMessage && outcome.FinalBack);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static CheckScreen Check(PasskeySignInViewModel screen)
    {
        return new CheckScreen(screen.State, screen.ErrorTitle, screen.ErrorMessage, screen.TryAgainCommand.CanExecute(null), screen.OffersBack,
            screen.SignInCommand.CanExecute(null));
    }

    [UnitTest]
    public void LeaveTheErrorOnceARetriedLinkWorks()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("taps create account while the app can't route it, then tries again once it can", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator { RegistrationFailure = new InvalidOperationException("route not found") };
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            Wait(screen.LoadAsync());
            Wait(screen.GoToRegistrationCommand.ExecuteAsync());
            PasskeyViewState afterFailure = screen.State;
            navigator.RegistrationFailure = null;
            Wait(screen.TryAgainCommand.ExecuteAsync());
            return new RetriedLinkOutcome(afterFailure, screen.State, screen.ErrorTitle, screen.TryAgainCommand.CanExecute(null), navigator.Calls.Count(c => c == "registration"));
        })
        .TheTest
        .ShouldPass<RetriedLinkOutcome>((because, outcome) =>
        {
            because.ItsTrue("the failed link shows a failure", outcome.AfterFailure == PasskeyViewState.Failed);
            because.ItsTrue("a retry that works clears it: " + outcome.Final, outcome.Final == PasskeyViewState.Ready && outcome.Title.Length == 0);
            because.ItsTrue("and takes its try again with it", !outcome.TryAgain);
            because.ItsTrue("the app is asked twice: " + outcome.Requests, outcome.Requests == 2);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void LetTheUserFixATypedUsername()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs in with a mistyped username, goes back, fixes it and continues", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            screen.Handle = "adda";
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.NoCredentials));
            Wait(screen.ContinueCommand.ExecuteAsync());
            TypedFailure failed = new TypedFailure(screen.State, screen.OffersTryAgain, screen.OffersBack, screen.OffersCreateAccountInstead, screen.BackCommand.CanExecute(null));
            Wait(screen.BackCommand.ExecuteAsync());
            PasskeyViewState afterBack = screen.State;
            string keptHandle = screen.Handle;
            bool backAfterBack = screen.BackCommand.CanExecute(null);
            screen.Handle = "ada";
            Wait(screen.ContinueCommand.ExecuteAsync());

            PasskeySignInViewModel button = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.NoCredentials));
            Wait(button.SignInCommand.ExecuteAsync());
            return new TypedOutcome(failed, afterBack, keptHandle, backAfterBack, client.LastHandle, navigator.Joined, button.OffersTryAgain, button.OffersBack);
        })
        .TheTest
        .ShouldPass<TypedOutcome>((because, outcome) =>
        {
            because.ItsTrue("a mistyped username fails", outcome.Failed.State == PasskeyViewState.Failed);
            because.ItsTrue("try again isn't offered: it would send the same username", !outcome.Failed.TryAgain);
            because.ItsTrue("back and create an account are offered", outcome.Failed.Back && outcome.Failed.CreateAccount && outcome.Failed.BackRuns);
            because.ItsTrue("back returns to the form", outcome.AfterBack == PasskeyViewState.Ready);
            because.ItsTrue("with the username kept: " + outcome.KeptHandle, outcome.KeptHandle == "adda");
            because.ItsTrue("back can't run on the form", !outcome.BackAfterBack);
            because.ItsTrue("the fixed username is the one sent: " + outcome.SentHandle, outcome.SentHandle == "ada");
            because.ItsTrue("and the user is signed in: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "signedIn");
            because.ItsTrue("without a typed username, try again is still offered", outcome.ButtonTryAgain && outcome.ButtonBack);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SurviveTheAppsNavigationFailing()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs in while the app's navigation throws, tries again once it works, and follows a broken link", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator { SignedInFailure = new InvalidOperationException("route //home not found") };
            CapturingLogger logger = new CapturingLogger();
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator, logger);
            string? escaped = null;
            try
            {
                Wait(screen.SignInCommand.ExecuteAsync());
            }
            catch (Exception thrown)
            {
                escaped = thrown.GetType().Name;
            }
            NavigationFailure broken = new NavigationFailure(screen.State, screen.ErrorMessage, screen.OffersTryAgain, screen.OffersBack, screen.SignInCommand.CanExecute(null));
            navigator.SignedInFailure = null;
            Wait(screen.TryAgainCommand.ExecuteAsync());
            PasskeyViewState afterRetry = screen.State;
            int ceremonies = client.Calls.Count(c => c == "authenticate");

            navigator.RegistrationFailure = new InvalidOperationException("no such page");
            Wait(screen.GoToRegistrationCommand.ExecuteAsync());
            PasskeyViewState afterBrokenLink = screen.State;
            Wait(screen.BackCommand.ExecuteAsync());

            FakePasskeyNavigator reshowNavigator = new FakePasskeyNavigator();
            PasskeySignInViewModel cached = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), reshowNavigator);
            PasskeyViewState afterLoad = PasskeyViewState.Failed;
            reshowNavigator.WhileSignedIn = () =>
            {
                Wait(cached.LoadAsync());
                afterLoad = cached.State;
            };
            Wait(cached.SignInCommand.ExecuteAsync());
            return new NavigationOutcome(escaped, broken, afterRetry, ceremonies, navigator.Joined, afterBrokenLink, screen.State, afterLoad,
                cached.SignInCommand.CanExecute(null), logger.AllText, screen.Configuration.Messages.Unknown);
        })
        .TheTest
        .ShouldPass<NavigationOutcome>((because, outcome) =>
        {
            because.ItsTrue("nothing escapes the command: " + outcome.Escaped, outcome.Escaped is null);
            because.ItsTrue("a navigation failure is a failure screen, not a blank success screen", outcome.Broken.State == PasskeyViewState.Failed);
            because.ItsTrue("with the configured message", outcome.Broken.Message == outcome.UnknownMessage);
            because.ItsTrue("with try again and back", outcome.Broken.TryAgain && outcome.Broken.Back);
            because.ItsTrue("try again navigates again and the screen is back on the form", outcome.AfterRetry == PasskeyViewState.Ready);
            because.ItsTrue("without a second ceremony: " + outcome.Ceremonies, outcome.Ceremonies == 1);
            because.ItsTrue("the navigator was called twice for the sign-in, then once for the link: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "signedIn,signedIn,registration");
            because.ItsTrue("a broken link is a failure screen too", outcome.AfterBrokenLink == PasskeyViewState.Failed);
            because.ItsTrue("and back returns to the form", outcome.AfterBack == PasskeyViewState.Ready);
            because.ItsTrue("a screen loaded again after a sign-in is on the form", outcome.AfterLoad == PasskeyViewState.Ready && outcome.SignInAfterLoad);
            because.ItsTrue("the failure is logged by exception type: " + outcome.Logged, outcome.Logged.Contains("InvalidOperationException", StringComparison.Ordinal));
            because.ItsTrue("without the exception's text", !outcome.Logged.Contains("//home", StringComparison.Ordinal) && !outcome.Logged.Contains("no such page", StringComparison.Ordinal));
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
        bool ContinueBlank, bool ContinueWhitespace, bool ContinueTyped, string NullHandle, string Title, PasskeyViewState State, bool ShowsForm, bool OffersRegistration, bool HasAlternative, bool BackOnForm);

    private sealed record SignedInOutcome(string ButtonCalls, string? SignedInHandle, PasskeyFlow? SignedInFrom, PasskeyViewState WhileNavigating, PasskeyViewState State, bool SignInAgain,
        string UsernameCalls, string? SentHandle, string ClientCalls);

    private sealed record CancelState(PasskeyViewState State, bool HasFailed, bool HasNote, string? Note, bool NoRecovery);

    private sealed record CancelOutcome(CancelState Dismissed, CancelState Cancelled, bool CancelBefore, PasskeyViewState WhileRunning, bool ShowsFormWhileRunning,
        bool CancelWhileRunning, bool SignInWhileRunning, bool TryAgainWhileRunning, bool HasNoteWhileRunning, bool CancelAfter, bool SignInAfter, string NavigatorCalls, string CancelledText);

    private sealed record FailedState(PasskeyViewState State, string Title, string Message, bool HasHint, bool TryAgain, bool CreateAccount, bool AnotherWay, bool SignInInstead, bool ShowsForm);

    private sealed record RecoveryOutcome(FailedState Failed, PasskeyViewState AfterLink, bool TryAgainAfterLinks, string NavigatorCalls, string ClientCalls, PasskeyViewState AfterRetry, string ExpectedTitle,
        PasskeyViewState DismissedState, string DismissedTitle, bool AnotherWayOnForm, string AlternativeLabel);

    private sealed record TypedFailure(PasskeyViewState State, bool TryAgain, bool Back, bool CreateAccount, bool BackRuns);

    private sealed record TypedOutcome(TypedFailure Failed, PasskeyViewState AfterBack, string KeptHandle, bool BackAfterBack, string? SentHandle, string NavigatorCalls, bool ButtonTryAgain, bool ButtonBack);

    private sealed record NavigationFailure(PasskeyViewState State, string Message, bool TryAgain, bool Back, bool SignIn);

    private sealed record NavigationOutcome(string? Escaped, NavigationFailure Broken, PasskeyViewState AfterRetry, int Ceremonies, string NavigatorCalls, PasskeyViewState AfterBrokenLink,
        PasskeyViewState AfterBack, PasskeyViewState AfterLoad, bool SignInAfterLoad, string Logged, string UnknownMessage);

    private sealed record NewerErrorOutcome(string FirstMessage, string NewerMessage, PasskeyViewState Final, string FinalMessage, bool FinalBack,
        string UnknownMessage);

    private sealed record CheckScreen(PasskeyViewState State, string Title, string Message, bool TryAgain, bool Back, bool SignIn)
    {
        public bool IsSetupScreen(string title, string message)
        {
            return State == PasskeyViewState.Failed && Title == title && Message == message && TryAgain && !Back && !SignIn;
        }
    }

    private sealed record LaterErrorOutcome(bool LinkFailureOffersBack, CheckScreen AfterBack, CheckScreen AfterRetriedLink, PasskeyViewState Final,
        bool SignInAfterSetup, string SetupTitle, string SetupMessage, int Requests);

    private sealed record RetriedLinkOutcome(PasskeyViewState AfterFailure, PasskeyViewState Final, string Title, bool TryAgain, int Requests);

    private sealed record UnsupportedState(PasskeyViewState State, string Title, string Message, bool HasHint, bool TryAgain, bool SignIn, bool Continue);

    private sealed record UnsupportedOutcome(UnsupportedState Missing, bool MissingBack, UnsupportedState AfterBack, UnsupportedState SetUp, bool OldBack,
        PasskeyViewState OldState, string OldMessage, string UnsupportedMessage, int CeremoniesWhileBlocked, int Probes, string OldTitle, bool OldTryAgain,
        bool OldSignIn, string SetupTitle, string UnavailableTitle, string MissingMessage);
}
