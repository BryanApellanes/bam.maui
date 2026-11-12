using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyRegistrationViewModel Should", Selector = "pkrv")]
public class PasskeyRegistrationViewModelShould : UnitTestMenuContainer
{
    public PasskeyRegistrationViewModelShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ShowSuccessOnlyAfterTheBackendVerifies()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("creates an account through the real client, then leaves the success screen", (fixture, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(fixture.Client(), fixture.Configuration, navigator);
            bool createBlank = screen.CreateCommand.CanExecute(null);
            screen.Handle = "ada";
            bool createWithoutName = screen.CreateCommand.CanExecute(null);
            screen.DisplayName = "Ada";
            bool createReady = screen.CreateCommand.CanExecute(null);
            bool doneBefore = screen.DoneCommand.CanExecute(null);
            Wait(screen.CreateCommand.ExecuteAsync());
            string navigatorAfterCreate = navigator.Joined;
            PasskeyViewState afterCreate = screen.State;
            bool doneAfter = screen.DoneCommand.CanExecute(null);
            Wait(screen.DoneCommand.ExecuteAsync());
            string cleared = screen.Handle + screen.DisplayName + screen.Email + screen.InviteCode;
            return new VerifiedOutcome(createBlank, createWithoutName, createReady, doneBefore, fixture.Log.Joined, afterCreate, navigatorAfterCreate,
                doneAfter, navigator.Joined, navigator.SignedIn?.Handle, navigator.SignedInFrom, screen.SuccessTitle, screen.Title, screen.Benefits.Count,
                screen.State, cleared, screen.DoneCommand.CanExecute(null));
        })
        .TheTest
        .ShouldPass<VerifiedOutcome>((because, outcome) =>
        {
            because.ItsTrue("create needs a username", !outcome.CreateBlank);
            because.ItsTrue("create needs a display name", !outcome.CreateWithoutName);
            because.ItsTrue("create runs with both", outcome.CreateReady);
            because.ItsTrue("done can't run before there is an account", !outcome.DoneBefore);
            because.ItsTrue("the backend verified the new credential: " + outcome.Log, outcome.Log.EndsWith("authenticator.create,backend.completeRegistration", StringComparison.Ordinal));
            because.ItsTrue("success shows after the backend verified", outcome.AfterCreate == PasskeyViewState.Succeeded);
            because.ItsTrue("creating does not navigate by itself: " + outcome.NavigatorAfterCreate, outcome.NavigatorAfterCreate.Length == 0);
            because.ItsTrue("done can run on the success screen", outcome.DoneAfter);
            because.ItsTrue("done navigates once: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "signedIn");
            because.ItsTrue("with the new account", outcome.SignedInHandle == "ada");
            because.ItsTrue("from registration", outcome.SignedInFrom == PasskeyFlow.Registration);
            because.ItsTrue("the success heading carries the app name: " + outcome.SuccessTitle, outcome.SuccessTitle == "Your Example account is ready");
            because.ItsTrue("the heading carries the app name: " + outcome.Title, outcome.Title == "Create your Example account");
            because.ItsTrue("the benefits show", outcome.Benefits > 0);
            because.ItsTrue("after done the screen is back on the form", outcome.AfterDone == PasskeyViewState.Ready);
            because.ItsTrue("with nothing of the account left on it: [" + outcome.Cleared + "]", outcome.Cleared.Length == 0);
            because.ItsTrue("and done can't run again", !outcome.DoneAgain);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NotShowSuccessWhenTheBackendRefuses()
    {
        After.Setup(reg => reg.Set(new PasskeyClientFixture()))
        .When<PasskeyClientFixture>("creates an account the backend refuses to verify", (fixture, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            fixture.Backend.Failures["backend.completeRegistration"] = new PasskeyException(PasskeyErrorCategory.ServerVerificationFailed, "attestation rejected");
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(fixture.Client(), fixture.Configuration, navigator);
            screen.Handle = "ada";
            screen.DisplayName = "Ada";
            Wait(screen.CreateCommand.ExecuteAsync());
            Wait(screen.DoneCommand.ExecuteAsync());
            return new RefusedOutcome(screen.State, screen.HasSucceeded, screen.DoneCommand.CanExecute(null), navigator.Joined, screen.ErrorTitle,
                screen.OffersTryAgain, screen.OffersSignInInstead, screen.OffersCreateAccountInstead, fixture.Configuration.Text.RegistrationFailedTitle, screen.OffersBack);
        })
        .TheTest
        .ShouldPass<RefusedOutcome>((because, outcome) =>
        {
            because.ItsTrue("a refused credential is a failure", outcome.State == PasskeyViewState.Failed && !outcome.HasSucceeded);
            because.ItsTrue("done can't run", !outcome.DoneEnabled);
            because.ItsTrue("nothing navigates: " + outcome.NavigatorCalls, outcome.NavigatorCalls.Length == 0);
            because.ItsTrue("the heading is the registration failure heading: " + outcome.Title, outcome.Title == outcome.ExpectedTitle);
            because.ItsTrue("a refused sign-up isn't sent again as it is: back to the form instead", !outcome.TryAgain && outcome.Back);
            because.ItsTrue("sign in instead is offered", outcome.SignInInstead);
            because.ItsTrue("create-account is not offered on the registration screen", !outcome.CreateAccount);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SendOnlyTheFieldsItAsksFor()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("creates accounts with padded fields, with and without email and invite code", (client, reg) =>
        {
            PasskeyRegistrationViewModel hidden = new PasskeyRegistrationViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            hidden.Handle = "  ada ";
            hidden.DisplayName = " Ada L ";
            hidden.Email = "ada@example.com";
            hidden.InviteCode = "invite";
            Wait(hidden.CreateCommand.ExecuteAsync());
            PasskeyRegistrationRequest hiddenRequest = client.LastRequest!;

            PasskeyConfiguration asking = PasskeyClientFixture.ValidConfiguration();
            asking.Experience.AskForEmail = true;
            asking.Experience.AskForInviteCode = true;
            PasskeyRegistrationViewModel shown = new PasskeyRegistrationViewModel(client, asking, new FakePasskeyNavigator());
            shown.Handle = "ada";
            shown.DisplayName = "Ada";
            shown.Email = " ada@example.com ";
            shown.InviteCode = "   ";
            Wait(shown.CreateCommand.ExecuteAsync());
            PasskeyRegistrationRequest shownRequest = client.LastRequest!;

            PasskeyRegistrationViewModel whitespace = new PasskeyRegistrationViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            whitespace.Handle = "   ";
            whitespace.DisplayName = "Ada";
            return new FieldsOutcome(hiddenRequest.Handle, hiddenRequest.DisplayName, hiddenRequest.Email, hiddenRequest.InviteCode, hidden.AsksForEmail, hidden.AsksForInviteCode,
                shownRequest.Email, shownRequest.InviteCode, shown.AsksForEmail, shown.AsksForInviteCode, whitespace.CreateCommand.CanExecute(null), hidden.OffersSignIn);
        })
        .TheTest
        .ShouldPass<FieldsOutcome>((because, outcome) =>
        {
            because.ItsTrue("the username is trimmed: [" + outcome.Handle + "]", outcome.Handle == "ada");
            because.ItsTrue("the display name is trimmed: [" + outcome.DisplayName + "]", outcome.DisplayName == "Ada L");
            because.ItsTrue("an email the screen doesn't ask for is not sent", outcome.HiddenEmail is null);
            because.ItsTrue("an invite code the screen doesn't ask for is not sent", outcome.HiddenInvite is null);
            because.ItsTrue("email and invite code are hidden by default", !outcome.AsksEmailByDefault && !outcome.AsksInviteByDefault);
            because.ItsTrue("an asked-for email is trimmed: [" + outcome.ShownEmail + "]", outcome.ShownEmail == "ada@example.com");
            because.ItsTrue("a blank invite code is not sent", outcome.ShownInvite is null);
            because.ItsTrue("the fields show when asked for", outcome.AsksEmail && outcome.AsksInvite);
            because.ItsTrue("a whitespace username can't create an account", !outcome.WhitespaceCreates);
            because.ItsTrue("sign-in is linked when authentication is on", outcome.OffersSignIn);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SendAnExistingAccountToSignIn()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("creates an account this device already holds a passkey for, then follows sign in instead", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            screen.Handle = "ada";
            screen.DisplayName = "Ada";
            client.SignInResults.Enqueue(ScriptedPasskeyClient.Failed(PasskeyErrorCategory.CredentialAlreadyRegistered));
            Wait(screen.CreateCommand.ExecuteAsync());
            bool tryAgain = screen.OffersTryAgain;
            bool signInInstead = screen.OffersSignInInstead;
            Wait(screen.GoToSignInCommand.ExecuteAsync());

            PasskeyConfiguration registrationOnly = PasskeyClientFixture.ValidConfiguration();
            registrationOnly.Features = new PasskeyFeatures { EnableAuthentication = false, EnableUsernameLessAuthentication = false };
            PasskeyRegistrationViewModel noSignIn = new PasskeyRegistrationViewModel(client, registrationOnly, new FakePasskeyNavigator());
            return new ExistingOutcome(tryAgain, signInInstead, navigator.Joined, noSignIn.OffersSignIn, noSignIn.GoToSignInCommand.CanExecute(null));
        })
        .TheTest
        .ShouldPass<ExistingOutcome>((because, outcome) =>
        {
            because.ItsTrue("trying again can't help an existing passkey", !outcome.TryAgain);
            because.ItsTrue("sign in instead is offered", outcome.SignInInstead);
            because.ItsTrue("the link reaches the navigator: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "signIn");
            because.ItsTrue("no sign-in link when authentication is off", !outcome.OffersSignInWhenOff && !outcome.GoToSignInWhenOff);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void LetTheUserChangeARefusedUsername()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("creates an account whose username is taken, goes back, changes it and creates again", (client, reg) =>
        {
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(client, PasskeyClientFixture.ValidConfiguration(), new FakePasskeyNavigator());
            screen.Handle = "ada";
            screen.DisplayName = "Ada";
            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Failure(new PasskeyError(PasskeyErrorCategory.InvalidOptions, "taken", "HandleTaken")));
            Wait(screen.CreateCommand.ExecuteAsync());
            bool tryAgain = screen.OffersTryAgain;
            bool back = screen.OffersBack;
            Wait(screen.BackCommand.ExecuteAsync());
            RefusedForm form = new RefusedForm(screen.State, screen.Handle, screen.DisplayName, screen.CreateCommand.CanExecute(null));
            screen.Handle = "ada.l";
            Wait(screen.CreateCommand.ExecuteAsync());
            return new ChangedOutcome(tryAgain, back, form, client.LastRequest?.Handle, screen.State);
        })
        .TheTest
        .ShouldPass<ChangedOutcome>((because, outcome) =>
        {
            because.ItsTrue("a taken username isn't tried again as it is", !outcome.TryAgain);
            because.ItsTrue("back is offered", outcome.Back);
            because.ItsTrue("back returns to the form", outcome.Form.State == PasskeyViewState.Ready);
            because.ItsTrue("with the fields kept", outcome.Form.Handle == "ada" && outcome.Form.DisplayName == "Ada");
            because.ItsTrue("and create can run", outcome.Form.CreateEnabled);
            because.ItsTrue("the changed username is the one sent: " + outcome.SentHandle, outcome.SentHandle == "ada.l");
            because.ItsTrue("and the account is created", outcome.Final == PasskeyViewState.Succeeded);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void KeepTheAccountWhenTheAppsNavigationFails()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("leaves the success screen while the app's navigation throws, goes back, then leaves once it works", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator { SignedInFailure = new InvalidOperationException("route not found") };
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            screen.Handle = "ada";
            screen.DisplayName = "Ada";
            Wait(screen.CreateCommand.ExecuteAsync());
            string? escaped = null;
            try
            {
                Wait(screen.DoneCommand.ExecuteAsync());
            }
            catch (Exception thrown)
            {
                escaped = thrown.GetType().Name;
            }
            PasskeyViewState afterFailedDone = screen.State;
            bool tryAgain = screen.OffersTryAgain;
            Wait(screen.BackCommand.ExecuteAsync());
            PasskeyViewState afterBack = screen.State;
            bool doneAfterBack = screen.DoneCommand.CanExecute(null);
            navigator.SignedInFailure = null;
            Wait(screen.DoneCommand.ExecuteAsync());
            return new DoneFailureOutcome(escaped, afterFailedDone, tryAgain, afterBack, doneAfterBack, navigator.Joined, navigator.SignedIn?.Handle, screen.State,
                client.Calls.Count(c => c == "register"));
        })
        .TheTest
        .ShouldPass<DoneFailureOutcome>((because, outcome) =>
        {
            because.ItsTrue("nothing escapes the command: " + outcome.Escaped, outcome.Escaped is null);
            because.ItsTrue("a failed done shows a failure with try again", outcome.AfterFailedDone == PasskeyViewState.Failed && outcome.TryAgain);
            because.ItsTrue("back returns to the success screen, not the form: the account exists", outcome.AfterBack == PasskeyViewState.Succeeded);
            because.ItsTrue("where done can run", outcome.DoneAfterBack);
            because.ItsTrue("done then reaches the app: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "signedIn,signedIn" && outcome.SignedInHandle == "ada");
            because.ItsTrue("and the screen is cleared", outcome.Final == PasskeyViewState.Ready);
            because.ItsTrue("the account is created once", outcome.Registrations == 1);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record VerifiedOutcome(bool CreateBlank, bool CreateWithoutName, bool CreateReady, bool DoneBefore, string Log, PasskeyViewState AfterCreate, string NavigatorAfterCreate,
        bool DoneAfter, string NavigatorCalls, string? SignedInHandle, PasskeyFlow? SignedInFrom, string SuccessTitle, string Title, int Benefits,
        PasskeyViewState AfterDone, string Cleared, bool DoneAgain);

    private sealed record RefusedForm(PasskeyViewState State, string Handle, string DisplayName, bool CreateEnabled);

    private sealed record ChangedOutcome(bool TryAgain, bool Back, RefusedForm Form, string? SentHandle, PasskeyViewState Final);

    private sealed record DoneFailureOutcome(string? Escaped, PasskeyViewState AfterFailedDone, bool TryAgain, PasskeyViewState AfterBack, bool DoneAfterBack, string NavigatorCalls,
        string? SignedInHandle, PasskeyViewState Final, int Registrations);

    private sealed record RefusedOutcome(PasskeyViewState State, bool HasSucceeded, bool DoneEnabled, string NavigatorCalls, string Title, bool TryAgain, bool SignInInstead,
        bool CreateAccount, string ExpectedTitle, bool Back);

    private sealed record FieldsOutcome(string Handle, string DisplayName, string? HiddenEmail, string? HiddenInvite, bool AsksEmailByDefault, bool AsksInviteByDefault,
        string? ShownEmail, string? ShownInvite, bool AsksEmail, bool AsksInvite, bool WhitespaceCreates, bool OffersSignIn);

    private sealed record ExistingOutcome(bool TryAgain, bool SignInInstead, string NavigatorCalls, bool OffersSignInWhenOff, bool GoToSignInWhenOff);
}
