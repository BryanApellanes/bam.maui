using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Passkey Hand-off Should", Selector = "pkho")]
public class PasskeyHandOffShould : UnitTestMenuContainer
{
    public PasskeyHandOffShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ShowSomethingWhileTheAppTakesOverAfterSignIn()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs in while the app's SignedInAsync looks at the screen", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeySignInViewModel screen = new PasskeySignInViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            HandOffShot? during = null;
            navigator.WhileSignedIn = () => during = new HandOffShot(screen.State, screen.HasSucceeded, screen.IsReady, screen.SignedInTitle, screen.SigningInText);
            Wait(screen.SignInCommand.ExecuteAsync());
            return new SignedInOutcome(during, screen.State);
        })
        .TheTest
        .ShouldPass<SignedInOutcome>((because, outcome) =>
        {
            because.ItsTrue("the app was handed the sign-in", outcome.During is not null);
            because.ItsTrue("while it runs the screen is on its signed-in state: " + outcome.During, outcome.During!.State == PasskeyViewState.Succeeded && outcome.During.Succeeded && !outcome.During.Ready);
            because.ItsTrue("with a heading naming the app: " + outcome.During.Title, outcome.During.Title == "You're signed in to Example");
            because.ItsTrue("and a line saying what is happening: " + outcome.During.Line, outcome.During.Line == new PasskeyText().SigningIn);
            because.ItsTrue("afterwards the screen is back on the form", outcome.After == PasskeyViewState.Ready);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void OfferToSaveAProfileSignUpCouldNotWrite()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("creates an account whose profile wasn't written, has the save refused, goes back, saves it, then leaves", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            configuration.Experience.AskForEmail = true;
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(client, configuration, navigator);
            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(ScriptedPasskeyClient.Ada with { ProfileCreated = false, DisplayName = "ada" }));
            screen.Handle = "ada";
            screen.DisplayName = "Ada";
            Wait(screen.CreateCommand.ExecuteAsync());
            bool offered = screen.ShowsProfileStep && screen.SaveProfileCommand.CanExecute(null) && screen.DoneCommand.CanExecute(null);
            string message = screen.ProfileMessage;

            client.SaveProfileResult = PasskeyResult.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "EmailRequired"));
            Wait(screen.SaveProfileCommand.ExecuteAsync());
            ProfileShot refused = Shot(screen);
            Wait(screen.BackCommand.ExecuteAsync());
            ProfileShot back = Shot(screen);

            screen.Email = " ada@example.com ";
            client.SaveProfileResult = PasskeyResult.Success();
            Wait(screen.SaveProfileCommand.ExecuteAsync());
            ProfileShot saved = Shot(screen);
            string profile = client.SavedProfile?.DisplayName + "|" + client.SavedProfile?.Email;
            Wait(screen.DoneCommand.ExecuteAsync());
            return new ProfileOutcome(offered, message, refused, back, saved, profile, navigator.SignedIn?.ProfileCreated, navigator.SignedIn?.DisplayName, screen.Email);
        })
        .TheTest
        .ShouldPass<ProfileOutcome>((because, outcome) =>
        {
            because.ItsTrue("the success screen offers to save the profile, and Done stays", outcome.Offered);
            because.ItsTrue("saying why: " + outcome.Message, outcome.Message == new PasskeyText().ProfileNotSavedMessage);
            because.ItsTrue("a refused profile is a profile failure, without try again: " + outcome.Refused, outcome.Refused.Failed && outcome.Refused.Title == "Couldn't save your details" && !outcome.Refused.TryAgain && outcome.Refused.Back);
            because.ItsTrue("Back returns to the success screen with the profile step: " + outcome.Back, outcome.Back.State == PasskeyViewState.Succeeded && outcome.Back.ShowsProfile && outcome.Back.DisplayName == "Ada");
            because.ItsTrue("a saved profile closes the step: " + outcome.Saved, outcome.Saved.State == PasskeyViewState.Succeeded && !outcome.Saved.ShowsProfile);
            because.ItsTrue("with the trimmed name and email: " + outcome.Profile, outcome.Profile == "Ada|ada@example.com");
            because.ItsTrue("the app is told the profile exists", outcome.HandedProfileCreated == true);
            because.ItsTrue("with the saved name", outcome.HandedName == "Ada");
            because.ItsTrue("and the screen is cleared", outcome.EmailAfter.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void LetThePersonLeaveWithoutSavingTheProfile()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("creates an account whose profile wasn't written, then leaves at once; then one whose profile was", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyRegistrationViewModel screen = new PasskeyRegistrationViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
            client.SignInResults.Enqueue(PasskeyResult<PasskeySignIn>.Success(ScriptedPasskeyClient.Ada with { ProfileCreated = false }));
            screen.Handle = "ada";
            screen.DisplayName = "Ada";
            Wait(screen.CreateCommand.ExecuteAsync());
            Wait(screen.DoneCommand.ExecuteAsync());
            bool? skipped = navigator.SignedIn?.ProfileCreated;

            screen.Handle = "bea";
            screen.DisplayName = "Bea";
            Wait(screen.CreateCommand.ExecuteAsync());
            bool offeredWhenWritten = screen.ShowsProfileStep || screen.SaveProfileCommand.CanExecute(null);
            return new SkipOutcome(skipped, offeredWhenWritten, string.Join(",", client.Calls));
        })
        .TheTest
        .ShouldPass<SkipOutcome>((because, outcome) =>
        {
            because.ItsTrue("leaving without saving tells the app the profile is missing", outcome.Skipped == false);
            because.ItsTrue("an account whose profile was written isn't asked", !outcome.OfferedWhenWritten);
            because.ItsTrue("no profile was saved: " + outcome.Calls, !outcome.Calls.Contains("saveProfile", StringComparison.Ordinal));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static ProfileShot Shot(PasskeyRegistrationViewModel screen)
    {
        return new ProfileShot(screen.State, screen.HasFailed, screen.ErrorTitle, screen.OffersTryAgain, screen.OffersBack, screen.ShowsProfileStep, screen.DisplayName);
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record HandOffShot(PasskeyViewState State, bool Succeeded, bool Ready, string Title, string Line);

    private sealed record SignedInOutcome(HandOffShot? During, PasskeyViewState After);

    private sealed record ProfileShot(PasskeyViewState State, bool Failed, string Title, bool TryAgain, bool Back, bool ShowsProfile, string DisplayName);

    private sealed record ProfileOutcome(bool Offered, string Message, ProfileShot Refused, ProfileShot Back, ProfileShot Saved, string Profile, bool? HandedProfileCreated, string? HandedName, string EmailAfter);

    private sealed record SkipOutcome(bool? Skipped, bool OfferedWhenWritten, string Calls);
}
