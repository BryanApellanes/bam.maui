using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

/// <summary>The management screen's account-key handover (bam.maui#7): the tag, the move panel, and each refusal.</summary>
[UnitTestMenu("Passkey Account Key Screen Should", Selector = "pkak")]
public class PasskeyAccountKeyScreenShould : UnitTestMenuContainer
{
    private static readonly PasskeyCredential Phone = new PasskeyCredential { CredentialId = "phone", Name = "Phone", IsCurrent = true, IsRegisteredKeySet = true };
    private static readonly PasskeyCredential Laptop = new PasskeyCredential { CredentialId = "laptop", Name = "Laptop", IsCurrent = false, IsRegisteredKeySet = false };
    private static readonly PasskeyCredential Tablet = new PasskeyCredential { CredentialId = "tablet", Name = "Tablet", IsCurrent = false, IsRegisteredKeySet = false };

    public PasskeyAccountKeyScreenShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void MoveTheAccountKeyBeforeRemovingItsPasskey()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("removes the session's key-set passkey, is asked to move the key, picks the tablet and confirms", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel screen = Screen(client, navigator);
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(Phone, Laptop, Tablet));
            Wait(screen.LoadAsync());
            string tags = string.Join(",", screen.Passkeys.Select(item => item.IsAccountKey ? item.AccountKeyText : "-"));

            Wait(screen.Passkeys[0].RemoveCommand.ExecuteAsync());
            string note = screen.RemovalAccountKeyNote + "|" + screen.HasRemovalWarning;
            Wait(screen.Passkeys[1].RemoveCommand.ExecuteAsync());
            bool noNoteForOthers = !screen.HasRemovalAccountKeyNote;
            Wait(screen.KeepCommand.ExecuteAsync());

            Wait(screen.Passkeys[0].RemoveCommand.ExecuteAsync());
            client.DeleteResults.Enqueue(Refused("KeySetHandoverRequired"));
            Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
            PanelShot opened = Panel(screen);

            screen.SelectedHeir = screen.HeirChoices[1];
            client.HandoverResult = PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" });
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            return new MoveOutcome(tags, note, noNoteForOthers, opened, client.HandoverId + "|" + client.HandoverHeir, string.Join(",", client.Calls), navigator.SignedOut?.NextUrl, screen.Passkeys.Count, screen.ShowsHandover, screen.HeirChoices.Count);
        })
        .TheTest
        .ShouldPass<MoveOutcome>((because, outcome) =>
        {
            because.ItsTrue("only the key-set passkey carries the tag: " + outcome.Tags, outcome.Tags == "Account key,-,-");
            because.ItsTrue("its removal confirmation says it holds the account key: " + outcome.Note, outcome.Note == new PasskeyText().RemoveAccountKeyMessage + "|True");
            because.ItsTrue("another passkey's doesn't", outcome.NoNoteForOthers);
            because.ItsTrue("a handover request opens the panel, not an error: " + outcome.Opened, outcome.Opened.Shows && outcome.Opened.State == PasskeyViewState.Ready && !outcome.Opened.Add);
            because.ItsTrue("offering every other passkey: " + outcome.Opened.Choices, outcome.Opened.Choices == "laptop,tablet");
            because.ItsTrue("starting with the first, since this session's passkey is the one going: " + outcome.Opened.Selected, outcome.Opened.Selected == "laptop");
            because.ItsTrue("the move names the removed passkey and the chosen heir: " + outcome.Sent, outcome.Sent == "phone|tablet");
            because.ItsTrue("after one list and one refused removal: " + outcome.Calls, outcome.Calls == "list,delete,handover");
            because.ItsTrue("a move that ended the session signs out through the app", outcome.SignedOutTo == "/account/signedout");
            because.ItsTrue("and clears the screen and the panel", outcome.Left == 0 && !outcome.PanelAfter && outcome.ChoicesAfter == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void HandleEachRefusedMove()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("moves the laptop's key to this session's passkey: refused heir, dismissed prompt, expired then retried, and a list that changed", (client, reg) =>
        {
            PasskeyCredential phone = Phone with { IsRegisteredKeySet = false };
            PasskeyCredential laptop = Laptop with { IsRegisteredKeySet = true };
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(phone, laptop, Tablet));
            Wait(screen.LoadAsync());
            OpenPanel(client, screen, 1);
            string selected = screen.SelectedHeir?.CredentialId ?? "none";

            client.HandoverResults.Enqueue(Refused("InvalidHeir"));
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            RefusalShot heir = Refusal(screen);
            Wait(screen.BackCommand.ExecuteAsync());
            bool heirBack = screen.ShowsHandover && !client.Calls.Skip(1).Contains("list");

            client.HandoverResults.Enqueue(PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.UserCancelled)));
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            bool dismissed = screen.IsReady && screen.ShowsHandover && screen.HasNote;

            client.HandoverResults.Enqueue(PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.ExpiredChallenge, "HandoverExpired")));
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            RefusalShot expired = Refusal(screen);
            int listsBeforeRetry = client.Calls.Count(call => call == "list");
            Wait(screen.TryAgainCommand.ExecuteAsync());
            string retried = client.HandoverHeir + "|" + (client.Calls.Count(call => call == "list") - listsBeforeRetry) + "|" + screen.ShowsHandover + "|" + screen.ShowsList;

            OpenPanel(client, screen, 1);
            client.HandoverResults.Enqueue(Refused("KeySetChanged"));
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            RefusalShot changed = Refusal(screen);
            int listsBeforeBack = client.Calls.Count(call => call == "list");
            Wait(screen.BackCommand.ExecuteAsync());
            string changedBack = (client.Calls.Count(call => call == "list") - listsBeforeBack) + "|" + screen.ShowsHandover + "|" + screen.ShowsList;

            OpenPanel(client, screen, 1);
            client.HandoverResults.Enqueue(Refused("LastCredential"));
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            RefusalShot last = Refusal(screen);
            return new RefusedMoveOutcome(selected, heir, heirBack, dismissed, expired, retried, changed, changedBack, last);
        })
        .TheTest
        .ShouldPass<RefusedMoveOutcome>((because, outcome) =>
        {
            PasskeyText text = new PasskeyText();
            because.ItsTrue("the key starts out going to this session's passkey: " + outcome.Selected, outcome.Selected == "phone");
            because.ItsTrue("a refused heir says so, without try again: " + outcome.Heir, outcome.Heir.Message == text.AccountKeyHeirRefusedMessage && !outcome.Heir.TryAgain && outcome.Heir.Back);
            because.ItsTrue("and Back returns to the panel without reloading", outcome.HeirBack);
            because.ItsTrue("a dismissed prompt stays on the panel quietly", outcome.Dismissed);
            because.ItsTrue("an expired handover offers try again: " + outcome.Expired, outcome.Expired.Failed && outcome.Expired.TryAgain);
            because.ItsTrue("which moves the key and reloads the list: " + outcome.Retried, outcome.Retried == "phone|1|False|True");
            because.ItsTrue("a list that changed says so, without try again: " + outcome.Changed, outcome.Changed.Message == text.PasskeysChangedMessage && !outcome.Changed.TryAgain && outcome.Changed.Back);
            because.ItsTrue("and Back loads the list again, with the panel closed: " + outcome.ChangedBack, outcome.ChangedBack == "1|False|True");
            because.ItsTrue("the last passkey is refused in the app's words", outcome.Last.Message == text.LastPasskeyMessage);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RemoveWithoutMovingOrKeepIt()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("opens the panel and keeps the passkey, opens it again and removes without moving, refused then accepted, then removes an ordinary passkey", (client, reg) =>
        {
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(Phone, Laptop));
            Wait(screen.LoadAsync());
            OpenPanel(client, screen, 0);
            bool commandsWhileOpen = screen.MoveAccountKeyCommand.CanExecute(null) && screen.RemoveWithoutMovingCommand.CanExecute(null);
            Wait(screen.CancelHandoverCommand.ExecuteAsync());
            bool cancelled = !screen.ShowsHandover && screen.ShowsAdd && screen.HeirChoices.Count == 0 && screen.SelectedHeir is null && !screen.MoveAccountKeyCommand.CanExecute(null);

            OpenPanel(client, screen, 0);
            client.DeleteResults.Enqueue(Refused("LastPasskey"));
            Wait(screen.RemoveWithoutMovingCommand.ExecuteAsync());
            RefusalShot refused = Refusal(screen);
            Wait(screen.BackCommand.ExecuteAsync());
            bool refusedBack = screen.ShowsHandover;

            client.DeleteResult = PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = false });
            Wait(screen.RemoveWithoutMovingCommand.ExecuteAsync());
            string removed = client.DeletedId + "|" + client.Calls[client.Calls.Count - 2] + "|" + client.Calls[client.Calls.Count - 1] + "|" + screen.ShowsHandover;

            Wait(screen.Passkeys[1].RemoveCommand.ExecuteAsync());
            Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
            string ordinary = client.Calls[client.Calls.Count - 2] + "|" + screen.ShowsHandover + "|" + client.Calls.Contains("handover");
            return new KeepOutcome(commandsWhileOpen, cancelled, refused, refusedBack, removed, ordinary);
        })
        .TheTest
        .ShouldPass<KeepOutcome>((because, outcome) =>
        {
            because.ItsTrue("the panel's actions are available while it shows", outcome.CommandsWhileOpen);
            because.ItsTrue("Keep closes it, and adding comes back", outcome.Cancelled);
            because.ItsTrue("a refused removal says so in the app's words: " + outcome.Refused, outcome.Refused.Message == new PasskeyText().LastPasskeyMessage);
            because.ItsTrue("and Back returns to the panel", outcome.RefusedBack);
            because.ItsTrue("removing without moving keeps the key set, then reloads: " + outcome.Removed, outcome.Removed == "phone|deleteKeepingKeySet|list|False");
            because.ItsTrue("an ordinary passkey is a plain removal with no panel: " + outcome.Ordinary, outcome.Ordinary == "delete|False|False");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    // Opens the removal confirmation for a row and has the backend ask for the account key to move first.
    private static void OpenPanel(ScriptedPasskeyClient client, PasskeyManagementViewModel screen, int row)
    {
        Wait(screen.Passkeys[row].RemoveCommand.ExecuteAsync());
        client.DeleteResults.Enqueue(Refused("KeySetHandoverRequired"));
        Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
    }

    private static PasskeyResult<PasskeySignOut> Refused(string code)
    {
        return PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, code));
    }

    private static PasskeyManagementViewModel Screen(ScriptedPasskeyClient client, IPasskeyNavigator navigator)
    {
        return new PasskeyManagementViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
    }

    private static PanelShot Panel(PasskeyManagementViewModel screen)
    {
        return new PanelShot(screen.ShowsHandover, screen.State, screen.ShowsAdd, string.Join(",", screen.HeirChoices.Select(item => item.CredentialId)), screen.SelectedHeir?.CredentialId ?? "none");
    }

    private static RefusalShot Refusal(PasskeyManagementViewModel screen)
    {
        return new RefusalShot(screen.HasFailed, screen.ErrorMessage, screen.OffersTryAgain, screen.OffersBack);
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record PanelShot(bool Shows, PasskeyViewState State, bool Add, string Choices, string Selected);

    private sealed record RefusalShot(bool Failed, string Message, bool TryAgain, bool Back);

    private sealed record MoveOutcome(string Tags, string Note, bool NoNoteForOthers, PanelShot Opened, string Sent, string Calls, string? SignedOutTo, int Left, bool PanelAfter, int ChoicesAfter);

    private sealed record RefusedMoveOutcome(string Selected, RefusalShot Heir, bool HeirBack, bool Dismissed, RefusalShot Expired, string Retried, RefusalShot Changed, string ChangedBack, RefusalShot Last);

    private sealed record KeepOutcome(bool CommandsWhileOpen, bool Cancelled, RefusalShot Refused, bool RefusedBack, string Removed, string Ordinary);
}
