using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyManagementViewModel Should", Selector = "pkmv")]
public class PasskeyManagementViewModelShould : UnitTestMenuContainer
{
    public PasskeyManagementViewModelShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void ListWhatTheBackendReports()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("loads two passkeys while the call is held open, then an account with none", (client, reg) =>
        {
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            bool addBeforeLoad = screen.AddCommand.CanExecute(null);
            client.Gate = new TaskCompletionSource<bool>();
            Task loading = screen.LoadAsync();
            ScreenShot whileLoading = Shot(screen);
            client.Gate.SetResult(true);
            client.Gate = null;
            Wait(loading);
            ScreenShot loaded = Shot(screen);
            PasskeyItem phone = screen.Passkeys[0];
            PasskeyItem laptop = screen.Passkeys[1];
            string rows = Row(phone) + ";" + Row(laptop);
            bool addAfterLoad = screen.AddCommand.CanExecute(null);

            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed());
            Wait(screen.RefreshCommand.ExecuteAsync());
            return new ListOutcome(addBeforeLoad, whileLoading, loaded, rows, phone.CanRemove && laptop.CanRemove, addAfterLoad, Shot(screen), screen.AddCommand.CanExecute(null),
                screen.Title, screen.Intro);
        })
        .TheTest
        .ShouldPass<ListOutcome>((because, outcome) =>
        {
            because.ItsTrue("nothing can be added before the list loads", !outcome.AddBeforeLoad);
            because.ItsTrue("loading shows progress with the update line, not the prompt line: " + outcome.WhileLoading, outcome.WhileLoading.State == PasskeyViewState.Working && outcome.WhileLoading.WorkingText == new PasskeyText().Updating);
            because.ItsTrue("and neither the list nor the empty state", !outcome.WhileLoading.List && !outcome.WhileLoading.Empty);
            because.ItsTrue("the loaded list shows: " + outcome.Loaded, outcome.Loaded.State == PasskeyViewState.Ready && outcome.Loaded.List && !outcome.Loaded.Empty && outcome.Loaded.Count == 2);
            because.ItsTrue("each row shows only what the backend reported: " + outcome.Rows, outcome.Rows == "Phone|added|used|current|no device;Passkey|added|-|-|no device");
            because.ItsTrue("with two passkeys either can be removed", outcome.BothRemovable);
            because.ItsTrue("adding is offered once loaded", outcome.AddAfterLoad);
            because.ItsTrue("an account with none shows the empty state: " + outcome.Empty, outcome.Empty.Empty && !outcome.Empty.List && outcome.Empty.Count == 0);
            because.ItsTrue("which offers to add one", outcome.AddWhenEmpty);
            because.ItsTrue("the heading: " + outcome.Title, outcome.Title == "Your passkeys");
            because.ItsTrue("the intro carries the app name: " + outcome.Intro, outcome.Intro.Contains("Example", StringComparison.Ordinal));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NeverShowAFailedListAsEmpty()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("fails to load offline, tries again, then loses the session", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel screen = Screen(client, navigator);
            client.ListResults.Enqueue(PasskeyResult<IReadOnlyList<PasskeyCredential>>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.NetworkFailure)));
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(ScriptedPasskeyClient.Phone));
            Wait(screen.LoadAsync());
            FailedShot offline = Failed(screen);
            Wait(screen.TryAgainCommand.ExecuteAsync());
            ScreenShot retried = Shot(screen);

            client.ListResults.Clear();
            client.ListResults.Enqueue(PasskeyResult<IReadOnlyList<PasskeyCredential>>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.NotSignedIn, "NotSignedIn")));
            Wait(screen.RefreshCommand.ExecuteAsync());
            FailedShot lost = Failed(screen);
            Wait(screen.GoToSignInCommand.ExecuteAsync());
            return new FailedListOutcome(offline, retried, lost, navigator.Joined);
        })
        .TheTest
        .ShouldPass<FailedListOutcome>((because, outcome) =>
        {
            because.ItsTrue("a list that couldn't load is a failure: " + outcome.Offline, outcome.Offline.Failed && outcome.Offline.Title == "You're offline");
            because.ItsTrue("in place of the list: no Back to an empty screen", !outcome.Offline.Back && !outcome.Offline.BackRuns);
            because.ItsTrue("and never the empty state", !outcome.Offline.Empty);
            because.ItsTrue("with try again", outcome.Offline.TryAgain);
            because.ItsTrue("which loads the list: " + outcome.Retried, outcome.Retried.List && outcome.Retried.Count == 1);
            because.ItsTrue("a lost session offers sign in again: " + outcome.Lost, outcome.Lost.Failed && outcome.Lost.SignInAgain && !outcome.Lost.TryAgain && !outcome.Lost.Back);
            because.ItsTrue("with the management heading", outcome.Lost.Title == "Couldn't update your passkeys");
            because.ItsTrue("which goes to sign-in: " + outcome.Navigation, outcome.Navigation == "signIn");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void AddAnotherPasskey()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("adds a named passkey with the prompt held open, then dismisses the prompt on a second", (client, reg) =>
        {
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(ScriptedPasskeyClient.Phone));
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(ScriptedPasskeyClient.Phone, ScriptedPasskeyClient.Laptop));
            Wait(screen.LoadAsync());
            bool lastRemovable = screen.Passkeys[0].RemoveCommand.CanExecute(null);
            screen.NewPasskeyName = "  Work laptop ";
            client.Gate = new TaskCompletionSource<bool>();
            Task adding = screen.AddCommand.ExecuteAsync();
            string promptLine = screen.WorkingText;
            client.Gate.SetResult(true);
            client.Gate = null;
            Wait(adding);
            ScreenShot afterAdd = Shot(screen);
            string calls = string.Join(",", client.Calls);
            string? addedName = client.AddedName;

            client.AddResult = PasskeyResult<string>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.UserCancelled));
            Wait(screen.AddCommand.ExecuteAsync());
            return new AddOutcome(lastRemovable, promptLine, addedName, afterAdd, screen.NewPasskeyName, calls, Shot(screen), screen.HasNote);
        })
        .TheTest
        .ShouldPass<AddOutcome>((because, outcome) =>
        {
            because.ItsTrue("an only passkey can't be removed", !outcome.LastRemovable);
            because.ItsTrue("adding shows the prompt line: " + outcome.PromptLine, outcome.PromptLine == new PasskeyText().Working);
            because.ItsTrue("the name goes with it, trimmed: " + outcome.AddedName, outcome.AddedName == "Work laptop");
            because.ItsTrue("then the list loads again: " + outcome.AfterAdd, outcome.AfterAdd.List && outcome.AfterAdd.Count == 2);
            because.ItsTrue("and the name field is cleared", outcome.NameAfterAdd.Length == 0);
            because.ItsTrue("list, add, list: " + outcome.Calls, outcome.Calls == "list,add,list");
            because.ItsTrue("a dismissed prompt leaves the list up: " + outcome.AfterCancel, outcome.AfterCancel.List && outcome.AfterCancel.State == PasskeyViewState.Ready);
            because.ItsTrue("with a note, not an error", outcome.CancelNote);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RenameAndKeepARefusedName()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("renames a passkey, has a second rename refused, goes back, then cancels", (client, reg) =>
        {
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            Wait(screen.LoadAsync());
            Wait(screen.Passkeys[1].RenameCommand.ExecuteAsync());
            bool editorOpen = screen.ShowsEditor;
            bool addHidden = !screen.ShowsAdd;
            string startName = screen.EditName;
            screen.EditName = " Work laptop ";
            Wait(screen.SaveNameCommand.ExecuteAsync());
            string renamed = client.RenamedId + "|" + client.RenamedTo;
            bool editorClosed = !screen.ShowsEditor && screen.Editing is null;

            Wait(screen.Passkeys[0].RenameCommand.ExecuteAsync());
            string phoneName = screen.EditName;
            screen.EditName = "x";
            client.RenameResult = PasskeyResult.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "BadRequest"));
            Wait(screen.SaveNameCommand.ExecuteAsync());
            FailedShot refused = Failed(screen);
            Wait(screen.BackCommand.ExecuteAsync());
            bool editorBack = screen.ShowsEditor && screen.EditName == "x";
            Wait(screen.CancelEditCommand.ExecuteAsync());
            return new RenameOutcome(editorOpen, addHidden, startName, renamed, editorClosed, phoneName, refused, editorBack, !screen.ShowsEditor && screen.ShowsAdd);
        })
        .TheTest
        .ShouldPass<RenameOutcome>((because, outcome) =>
        {
            because.ItsTrue("rename opens the editor", outcome.EditorOpen);
            because.ItsTrue("and hides adding", outcome.AddHidden);
            because.ItsTrue("an unnamed passkey starts blank: [" + outcome.StartName + "]", outcome.StartName.Length == 0);
            because.ItsTrue("the trimmed name is sent: " + outcome.Renamed, outcome.Renamed == "laptop|Work laptop");
            because.ItsTrue("and the editor closes when it works", outcome.EditorClosed);
            because.ItsTrue("a named passkey starts with its name: " + outcome.PhoneName, outcome.PhoneName == "Phone");
            because.ItsTrue("a refused name is a failure without try again: " + outcome.Refused, outcome.Refused.Failed && !outcome.Refused.TryAgain && outcome.Refused.Back);
            because.ItsTrue("Back returns to the editor with the name kept", outcome.EditorBack);
            because.ItsTrue("cancel closes it", outcome.Cancelled);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void AskBeforeRemovingAndSayWhenItSignsOut()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("asks to remove each passkey, keeps one, is refused for the last, then removes the session's passkey", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel screen = Screen(client, navigator);
            Wait(screen.LoadAsync());
            Wait(screen.Passkeys[1].RemoveCommand.ExecuteAsync());
            bool askedLaptop = screen.ShowsRemoval && !screen.HasRemovalWarning;
            Wait(screen.KeepCommand.ExecuteAsync());
            bool kept = !screen.ShowsRemoval && client.DeletedId is null;

            Wait(screen.Passkeys[1].RemoveCommand.ExecuteAsync());
            client.DeleteResult = PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "LastPasskey"));
            Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
            FailedShot last = Failed(screen);
            string lastMessage = screen.ErrorMessage;
            Wait(screen.BackCommand.ExecuteAsync());
            bool backToList = screen.ShowsList && !screen.ShowsRemoval;

            Wait(screen.Passkeys[0].RemoveCommand.ExecuteAsync());
            string warning = screen.RemovalWarning;
            client.DeleteResult = PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" });
            Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
            return new RemoveOutcome(askedLaptop, kept, last, lastMessage, backToList, warning, client.DeletedId, navigator.Joined, navigator.SignedOut?.NextUrl, Shot(screen), screen.IsLoaded);
        })
        .TheTest
        .ShouldPass<RemoveOutcome>((because, outcome) =>
        {
            because.ItsTrue("remove asks first, with no warning for another device's passkey", outcome.AskedLaptop);
            because.ItsTrue("keep removes nothing", outcome.Kept);
            because.ItsTrue("a refused last passkey says why: " + outcome.LastMessage, outcome.Last.Failed && outcome.LastMessage == new PasskeyText().LastPasskeyMessage);
            because.ItsTrue("without try again", !outcome.Last.TryAgain && outcome.Last.Back);
            because.ItsTrue("Back returns to the list, not the question", outcome.BackToList);
            because.ItsTrue("the session's passkey warns that removing it signs out: " + outcome.Warning, outcome.Warning == new PasskeyText().RemoveCurrentMessage);
            because.ItsTrue("the passkey is removed", outcome.Deleted == "phone");
            because.ItsTrue("and the app hears the session ended: " + outcome.Navigation, outcome.Navigation == "signedOut");
            because.ItsTrue("with where to go", outcome.NextUrl == "/account/signedout");
            because.ItsTrue("and the screen keeps nothing of the account: " + outcome.After, outcome.After.Count == 0 && !outcome.After.List && !outcome.After.Empty && !outcome.Loaded);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SignOutThroughTheNavigator()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs out with an app navigator, then with one written before SignedOutAsync existed, then offline", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel screen = Screen(client, navigator);
            Wait(screen.LoadAsync());
            Wait(screen.SignOutCommand.ExecuteAsync());
            string calls = string.Join(",", client.Calls);

            OlderPasskeyNavigator older = new OlderPasskeyNavigator();
            PasskeyManagementViewModel olderScreen = Screen(client, older);
            Wait(olderScreen.LoadAsync());
            Wait(olderScreen.SignOutCommand.ExecuteAsync());

            client.SignOutResult = PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.NetworkFailure));
            FakePasskeyNavigator offlineNavigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel offline = Screen(client, offlineNavigator);
            Wait(offline.LoadAsync());
            Wait(offline.SignOutCommand.ExecuteAsync());
            OfflineShot offlineShot = new OfflineShot(offline.HasFailed, offline.State, offline.Passkeys.Count, offline.ShowsList, offline.ShowsEmptyState, offline.ShowsEditor,
                offlineNavigator.Joined, offlineNavigator.SignedOut?.SignedOut, offlineNavigator.SignedOut?.NextUrl);
            return new SignOutOutcome(calls, navigator.Joined, navigator.SignedOut?.NextUrl, screen.Passkeys.Count, string.Join(",", older.Calls), offlineShot);
        })
        .TheTest
        .ShouldPass<SignOutOutcome>((because, outcome) =>
        {
            because.ItsTrue("sign-out calls the client: " + outcome.Calls, outcome.Calls == "list,signOut");
            because.ItsTrue("then the app: " + outcome.Navigation, outcome.Navigation == "signedOut");
            because.ItsTrue("with where to go", outcome.NextUrl == "/account/signedout");
            because.ItsTrue("and clears the list", outcome.Left == 0);
            because.ItsTrue("an older navigator shows sign-in: " + outcome.OlderNavigation, outcome.OlderNavigation == "signIn");
            because.ItsTrue("a sign-out that fails still signs out on this device, with no failure screen: " + outcome.Offline, !outcome.Offline.Failed && outcome.Offline.State == PasskeyViewState.Ready);
            because.ItsTrue("the account leaves the screen", outcome.Offline.Count == 0 && !outcome.Offline.List && !outcome.Offline.Empty && !outcome.Offline.Editor);
            because.ItsTrue("and the app is told, with no next URL: " + outcome.Offline, outcome.Offline.Navigation == "signedOut" && outcome.Offline.SignedOut == true && outcome.Offline.NextUrl is null);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ClearTheAccountBeforeTheAppTakesOver()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("signs out while the app's SignedOutAsync looks at the screen, then with a navigator that throws and goes Back, then loses the session renaming", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel screen = Screen(client, navigator);
            Wait(screen.LoadAsync());
            HandOffShot? during = null;
            navigator.WhileSignedOut = () => during = new HandOffShot(screen.ShowsSigningOut, screen.IsReady, screen.Passkeys.Count, screen.SignedOutTitle, screen.SigningOutText);
            Wait(screen.SignOutCommand.ExecuteAsync());
            HandOffShot after = new HandOffShot(screen.ShowsSigningOut, screen.IsReady, screen.Passkeys.Count, screen.SignedOutTitle, screen.SigningOutText);

            FakePasskeyNavigator broken = new FakePasskeyNavigator { SignedOutFailure = new InvalidOperationException("no shell") };
            PasskeyManagementViewModel brokenScreen = Screen(client, broken);
            Wait(brokenScreen.LoadAsync());
            Wait(brokenScreen.SignOutCommand.ExecuteAsync());
            FailedShot failed = Failed(brokenScreen);
            int failedCount = brokenScreen.Passkeys.Count;
            Wait(brokenScreen.BackCommand.ExecuteAsync());
            ScreenShot back = Shot(brokenScreen);

            client.RenameResult = PasskeyResult.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.NotSignedIn));
            PasskeyManagementViewModel lost = Screen(client, new FakePasskeyNavigator());
            Wait(lost.LoadAsync());
            Wait(lost.Passkeys[0].RenameCommand.ExecuteAsync());
            lost.EditName = "Work";
            Wait(lost.SaveNameCommand.ExecuteAsync());
            FailedShot lostShot = Failed(lost);
            int lostCount = lost.Passkeys.Count;
            Wait(lost.BackCommand.ExecuteAsync());
            ScreenShot lostBack = Shot(lost);

            PasskeyConfiguration otherSignIn = PasskeyClientFixture.ValidConfiguration();
            otherSignIn.Features.EnableAuthentication = false;
            PasskeyManagementViewModel elsewhere = new PasskeyManagementViewModel(client, otherSignIn, new FakePasskeyNavigator());
            Wait(elsewhere.LoadAsync());
            Wait(elsewhere.Passkeys[0].RenameCommand.ExecuteAsync());
            elsewhere.EditName = "Work";
            Wait(elsewhere.SaveNameCommand.ExecuteAsync());
            FailedShot elsewhereShot = Failed(elsewhere);
            Wait(elsewhere.BackCommand.ExecuteAsync());
            ScreenShot elsewhereBack = Shot(elsewhere);
            return new HandOffOutcome(during, after, failed, failedCount, back, lostShot, lostCount, lostBack, lost.ShowsEditor, elsewhereShot, elsewhereBack, elsewhere.ShowsEditor);
        })
        .TheTest
        .ShouldPass<HandOffOutcome>((because, outcome) =>
        {
            because.ItsTrue("the app was handed the sign-out", outcome.During is not null);
            because.ItsTrue("while it runs the screen shows the signed-out panel, not a blank page: " + outcome.During, outcome.During!.SigningOut && !outcome.During.Ready);
            because.ItsTrue("and the account is already gone", outcome.During.Count == 0);
            because.ItsTrue("with a heading and a line: " + outcome.During, outcome.During.Title == "You're signed out" && outcome.During.Line == new PasskeyText().SigningOut);
            because.ItsTrue("afterwards the panel is gone: " + outcome.After, !outcome.After.SigningOut && outcome.After.Ready && outcome.After.Count == 0);
            because.ItsTrue("a navigator that throws shows a failure: " + outcome.Failed, outcome.Failed.Failed && outcome.Failed.Back);
            because.ItsTrue("with the account already cleared", outcome.FailedCount == 0);
            because.ItsTrue("and Back doesn't bring the old list back: " + outcome.Back, outcome.Back.State == PasskeyViewState.Ready && !outcome.Back.List && outcome.Back.Count == 0);
            because.ItsTrue("a session lost while renaming offers sign in again: " + outcome.Lost, outcome.Lost.Failed && outcome.Lost.SignInAgain);
            because.ItsTrue("and takes the list off the screen", outcome.LostCount == 0);
            because.ItsTrue("so Back finds no list or editor: " + outcome.LostBack, !outcome.LostBack.List && outcome.LostBack.Count == 0 && !outcome.LostEditor);
            because.ItsTrue("an app that signs in another way gets try again for the lost session: " + outcome.Elsewhere, outcome.Elsewhere.Failed && !outcome.Elsewhere.SignInAgain && outcome.Elsewhere.TryAgain);
            because.ItsTrue("and the list still leaves the screen: " + outcome.ElsewhereBack, !outcome.ElsewhereBack.List && outcome.ElsewhereBack.Count == 0 && !outcome.ElsewhereEditor);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static PasskeyManagementViewModel Screen(ScriptedPasskeyClient client, IPasskeyNavigator navigator)
    {
        return new PasskeyManagementViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
    }

    private static ScreenShot Shot(PasskeyManagementViewModel screen)
    {
        return new ScreenShot(screen.State, screen.ShowsList, screen.ShowsEmptyState, screen.Passkeys.Count, screen.WorkingText);
    }

    private static FailedShot Failed(PasskeyManagementViewModel screen)
    {
        return new FailedShot(screen.HasFailed, screen.ErrorTitle, screen.OffersTryAgain, screen.OffersBack, screen.BackCommand.CanExecute(null), screen.OffersSignInAgain, screen.ShowsEmptyState);
    }

    private static string Row(PasskeyItem item)
    {
        return item.Title + "|" + (item.HasAddedText ? "added" : "-") + "|" + (item.HasLastUsedText ? "used" : "-") + "|" + (item.IsCurrent && item.CurrentText.Length > 0 ? "current" : "-") + "|" + (item.HasDeviceText ? item.DeviceText : "no device");
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record ScreenShot(PasskeyViewState State, bool List, bool Empty, int Count, string WorkingText);

    private sealed record FailedShot(bool Failed, string Title, bool TryAgain, bool Back, bool BackRuns, bool SignInAgain, bool Empty);

    private sealed record ListOutcome(bool AddBeforeLoad, ScreenShot WhileLoading, ScreenShot Loaded, string Rows, bool BothRemovable, bool AddAfterLoad, ScreenShot Empty, bool AddWhenEmpty, string Title, string Intro);

    private sealed record FailedListOutcome(FailedShot Offline, ScreenShot Retried, FailedShot Lost, string Navigation);

    private sealed record AddOutcome(bool LastRemovable, string PromptLine, string? AddedName, ScreenShot AfterAdd, string NameAfterAdd, string Calls, ScreenShot AfterCancel, bool CancelNote);

    private sealed record RenameOutcome(bool EditorOpen, bool AddHidden, string StartName, string Renamed, bool EditorClosed, string PhoneName, FailedShot Refused, bool EditorBack, bool Cancelled);

    private sealed record RemoveOutcome(bool AskedLaptop, bool Kept, FailedShot Last, string LastMessage, bool BackToList, string Warning, string? Deleted, string Navigation, string? NextUrl, ScreenShot After, bool Loaded);

    private sealed record SignOutOutcome(string Calls, string Navigation, string? NextUrl, int Left, string OlderNavigation, OfflineShot Offline);

    private sealed record OfflineShot(bool Failed, PasskeyViewState State, int Count, bool List, bool Empty, bool Editor, string Navigation, bool? SignedOut, string? NextUrl);

    private sealed record HandOffShot(bool SigningOut, bool Ready, int Count, string Title, string Line);

    private sealed record HandOffOutcome(HandOffShot? During, HandOffShot After, FailedShot Failed, int FailedCount, ScreenShot Back, FailedShot Lost, int LostCount, ScreenShot LostBack, bool LostEditor, FailedShot Elsewhere, ScreenShot ElsewhereBack, bool ElsewhereEditor);
}
