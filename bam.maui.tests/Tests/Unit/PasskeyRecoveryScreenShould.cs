using System.Globalization;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

/// <summary>The management screen's lost-device recovery (bam.maui#20): the row and panel actions, the recovery panel, the waiting notice, and each refusal.</summary>
[UnitTestMenu("Passkey Recovery Screen Should", Selector = "pkrs")]
public class PasskeyRecoveryScreenShould : UnitTestMenuContainer
{
    private static readonly PasskeyCredential Phone = new PasskeyCredential { CredentialId = "phone", Name = "Phone", IsCurrent = true, IsRegisteredKeySet = false };
    private static readonly PasskeyCredential Laptop = new PasskeyCredential { CredentialId = "laptop", Name = "Laptop", IsCurrent = false, IsRegisteredKeySet = true };
    private static readonly PasskeyCredential Tablet = new PasskeyCredential { CredentialId = "tablet", Name = "Tablet", IsCurrent = false, IsRegisteredKeySet = false };
    private static readonly DateTime Effective = new DateTime(2026, 11, 29, 12, 0, 0, DateTimeKind.Utc);

    public PasskeyRecoveryScreenShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void RecoverTheKeyOfALostPasskey()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("marks the laptop lost, recovers to this session's passkey, then sees the waiting notice and cancels it", (client, reg) =>
        {
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            NextList(client, Phone, Laptop, Tablet);
            Wait(screen.LoadAsync());
            string offers = string.Join(",", screen.Passkeys.Select(item => item.OffersLost + ":" + item.LostCommand.CanExecute(null)));
            bool noNotice = !screen.ShowsPendingRecovery && !screen.ShowsRecoverable && !screen.ShowsRecovery;

            Wait(screen.Passkeys[1].LostCommand.ExecuteAsync());
            PanelShot opened = Panel(screen);

            client.PendingRecovery = new PasskeyPendingRecovery { RecoveryId = "recovery-1", RemovedCredentialId = "laptop", HeirCredentialId = "phone", EffectiveUtc = Effective };
            NextList(client, Phone, Tablet);
            Wait(screen.StartRecoveryCommand.ExecuteAsync());
            string sent = client.RecoveryHeir + "|" + client.RecoveryRemoves;
            string notice = screen.ShowsPendingRecovery + "|" + screen.PendingRecoveryText;
            string blocked = screen.OpenRecoveryCommand.CanExecute(null) + "|" + screen.Passkeys.Any(item => item.LostCommand.CanExecute(null));
            bool panelGone = !screen.ShowsRecovery && screen.HeirChoices.Count == 0;

            client.PendingRecovery = null;
            NextList(client, Phone, Tablet);
            Wait(screen.CancelRecoveryCommand.ExecuteAsync());
            return new RecoverOutcome(offers, noNotice, opened, sent, notice, blocked, panelGone, client.CancelledRecoveryId, screen.ShowsPendingRecovery, string.Join(",", client.Calls));
        })
        .TheTest
        .ShouldPass<RecoverOutcome>((because, outcome) =>
        {
            because.ItsTrue("only the account-key passkey offers \"This device is lost\": " + outcome.Offers, outcome.Offers == "False:False,True:True,False:False");
            because.ItsTrue("with nothing waiting, no notice or panel shows", outcome.NoNotice);
            because.ItsTrue("the panel opens naming the lost passkey: " + outcome.Opened, outcome.Opened.Shows && outcome.Opened.Removes == "laptop" && !outcome.Opened.Add);
            because.ItsTrue("offering the others, with this session's passkey chosen: " + outcome.Opened.Choices + " " + outcome.Opened.Selected, outcome.Opened.Choices == "phone,tablet" && outcome.Opened.Selected == "phone" && outcome.Opened.StartEnabled && outcome.Opened.KeepEnabled);
            because.ItsTrue("the recovery names the heir and the lost passkey: " + outcome.Sent, outcome.Sent == "phone|laptop");
            string expected = new PasskeyText().PendingRecoveryMessage.Replace("{1}", Effective.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)).Replace("{0}", "Phone");
            because.ItsTrue("the list then shows the waiting move with the heir's name and date: " + outcome.Notice, outcome.Notice == "True|" + expected);
            because.ItsTrue("and no other recovery can start while it waits: " + outcome.Blocked, outcome.Blocked == "False|False");
            because.ItsTrue("the panel closed", outcome.PanelGone);
            because.ItsTrue("cancel names the waiting recovery and loads the list again", outcome.CancelledId == "recovery-1" && !outcome.NoticeAfter);
            because.ItsTrue("list, recover, list, cancel, list: " + outcome.Calls, outcome.Calls == "list,recover,list,cancelRecovery,list");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void OpenTheRecoveryFromTheMovePanelOrWhenNoPasskeyHoldsTheKey()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("is asked to move the laptop's key and says the device is lost; then, on a list where no passkey holds the key, recovers with the only passkey; then removes a lost passkey without moving the key", (client, reg) =>
        {
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            PasskeyManagementViewModel screen = Screen(client, navigator);
            NextList(client, Phone, Laptop, Tablet);
            Wait(screen.LoadAsync());
            Wait(screen.Passkeys[1].RemoveCommand.ExecuteAsync());
            client.DeleteResults.Enqueue(PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "KeySetHandoverRequired")));
            Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
            bool lostOffered = screen.ShowsHandover && screen.LostDeviceCommand.CanExecute(null);
            Wait(screen.LostDeviceCommand.ExecuteAsync());
            string fromMove = screen.ShowsHandover + "|" + Panel(screen);
            Wait(screen.CloseRecoveryCommand.ExecuteAsync());
            string closed = screen.ShowsRecovery + "|" + screen.HeirChoices.Count + "|" + screen.ShowsAdd;

            client.KeySetRecoverable = true;
            NextList(client, Phone);
            Wait(screen.LoadAsync());
            string recoverable = screen.ShowsRecoverable + "|" + screen.OpenRecoveryCommand.CanExecute(null);
            Wait(screen.OpenRecoveryCommand.ExecuteAsync());
            PanelShot unnamed = Panel(screen);
            bool hiddenWhileOpen = !screen.ShowsRecoverable;
            client.RecoveryResult = PasskeyResult<PasskeyRecoveryStarted>.Success(new PasskeyRecoveryStarted { SignOut = new PasskeySignOut { SignedOut = true, NextUrl = "/account/signedout" }, EffectiveUtc = Effective });
            Wait(screen.StartRecoveryCommand.ExecuteAsync());
            string sent = client.RecoveryHeir + "|" + (client.RecoveryRemoves ?? "none");

            client.KeySetRecoverable = false;
            PasskeyManagementViewModel second = Screen(client, new FakePasskeyNavigator());
            NextList(client, Phone, Laptop);
            Wait(second.LoadAsync());
            Wait(second.Passkeys[1].LostCommand.ExecuteAsync());
            NextList(client, Phone);
            Wait(second.RemoveLostWithoutMovingCommand.ExecuteAsync());
            return new OpenOutcome(lostOffered, fromMove, closed, recoverable, unnamed, hiddenWhileOpen, sent, navigator.SignedOut?.NextUrl,
                client.DeletedId + "|" + client.Calls[client.Calls.Count - 2], second.ShowsRecovery);
        })
        .TheTest
        .ShouldPass<OpenOutcome>((because, outcome) =>
        {
            because.ItsTrue("the move panel offers \"This device is lost\"", outcome.LostOffered);
            because.ItsTrue("which swaps it for the recovery panel naming that passkey: " + outcome.FromMove, outcome.FromMove.StartsWith("False|PanelShot { Shows = True, Removes = laptop, Choices = phone,tablet, Selected = phone", StringComparison.Ordinal));
            because.ItsTrue("cancel closes it and the list comes back: " + outcome.Closed, outcome.Closed == "False|0|True");
            because.ItsTrue("with no passkey holding the key the screen offers recovering it: " + outcome.Recoverable, outcome.Recoverable == "True|True");
            because.ItsTrue("the panel names no lost passkey and chooses the only one: " + outcome.Unnamed, outcome.Unnamed.Shows && outcome.Unnamed.Removes == "none" && outcome.Unnamed.Choices == "phone" && outcome.Unnamed.Selected == "phone" && !outcome.Unnamed.KeepEnabled);
            because.ItsTrue("and the offer hides while it's open", outcome.HiddenWhileOpen);
            because.ItsTrue("the recovery sends only the heir: " + outcome.Sent, outcome.Sent == "phone|none");
            because.ItsTrue("a recovery that ended the session signs out through the app", outcome.SignedOutTo == "/account/signedout");
            because.ItsTrue("removing without moving keeps the key: " + outcome.Kept, outcome.Kept == "laptop|deleteKeepingKeySet");
            because.ItsTrue("and closes the panel", !outcome.PanelAfterKeep);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void HandleEachRefusedRecovery()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("recovers the laptop's key and is refused: too new with and without a date, refused heir, another recovery waiting, the list changed, recovery not offered here; then cancels one that's gone", (client, reg) =>
        {
            PasskeyText text = new PasskeyText();
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            NextList(client, Phone, Laptop, Tablet);
            Wait(screen.LoadAsync());
            Wait(screen.Passkeys[1].LostCommand.ExecuteAsync());

            DateTime eligible = new DateTime(2026, 12, 3, 8, 0, 0, DateTimeKind.Utc);
            client.RecoveryResults.Enqueue(PasskeyResult<PasskeyRecoveryStarted>.Failure(new PasskeyError(PasskeyErrorCategory.InvalidOptions, "x", "HeirTooNew", eligible)));
            Wait(screen.StartRecoveryCommand.ExecuteAsync());
            RefusalShot tooNew = Refusal(screen);
            string tooNewBack = BackReloads(client, screen);

            client.RecoveryResults.Enqueue(Refused("HeirTooNew"));
            Wait(screen.StartRecoveryCommand.ExecuteAsync());
            RefusalShot tooNewLater = Refusal(screen);
            BackReloads(client, screen);

            client.RecoveryResults.Enqueue(Refused("InvalidHeir"));
            Wait(screen.StartRecoveryCommand.ExecuteAsync());
            RefusalShot heir = Refusal(screen);
            string heirBack = BackReloads(client, screen);

            string[] reloading = { "RecoveryPending", "HeirGone", "KeySetChanged", "NotFound", "RotationUnavailable" };
            List<string> reloads = new List<string>();
            foreach (string code in reloading)
            {
                if (!screen.ShowsRecovery)
                {
                    Wait(screen.Passkeys[1].LostCommand.ExecuteAsync());
                }
                client.RecoveryResults.Enqueue(Refused(code));
                Wait(screen.StartRecoveryCommand.ExecuteAsync());
                RefusalShot shot = Refusal(screen);
                NextList(client, Phone, Laptop, Tablet);
                reloads.Add(code + ":" + shot.Message + ":" + BackReloads(client, screen));
            }

            client.PendingRecovery = new PasskeyPendingRecovery { RecoveryId = "recovery-1", HeirCredentialId = "phone", EffectiveUtc = Effective };
            NextList(client, Phone, Tablet);
            Wait(screen.LoadAsync());
            client.CancelRecoveryResult = PasskeyResult.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "NotFound"));
            Wait(screen.CancelRecoveryCommand.ExecuteAsync());
            RefusalShot gone = Refusal(screen);
            client.PendingRecovery = null;
            NextList(client, Phone, Tablet);
            string goneBack = BackReloads(client, screen) + "|" + screen.ShowsPendingRecovery;
            return new RefusedOutcome(tooNew, tooNewBack, tooNewLater, heir, heirBack, string.Join("; ", reloads), gone, goneBack,
                text.RecoveryHeirTooNewMessage.Replace("{0}", eligible.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        })
        .TheTest
        .ShouldPass<RefusedOutcome>((because, outcome) =>
        {
            PasskeyText text = new PasskeyText();
            because.ItsTrue("a too-new heir says when it can take over: " + outcome.TooNew.Message, outcome.TooNew.Failed && outcome.TooNew.Message == outcome.TooNewExpected && outcome.TooNew.Back);
            because.ItsTrue("and Back returns to the panel to choose another, without a reload: " + outcome.TooNewBack, outcome.TooNewBack == "0|True");
            because.ItsTrue("without a date it still says why: " + outcome.TooNewLater.Message, outcome.TooNewLater.Message == text.RecoveryHeirTooNewLaterMessage);
            because.ItsTrue("a refused heir says so and goes back to the panel: " + outcome.HeirBack, outcome.Heir.Message == text.AccountKeyHeirRefusedMessage && outcome.HeirBack == "0|True");
            string expected = string.Join("; ",
                "RecoveryPending:" + text.RecoveryPendingMessage + ":1|False",
                "HeirGone:" + text.PasskeysChangedMessage + ":1|False",
                "KeySetChanged:" + text.PasskeysChangedMessage + ":1|False",
                "NotFound:" + text.PasskeysChangedMessage + ":1|False",
                "RotationUnavailable:" + text.RecoveryUnavailableMessage + ":1|False");
            because.ItsTrue("each refusal that means the list is out of date says so and reloads on Back: " + outcome.Reloads, outcome.Reloads == expected);
            because.ItsTrue("cancelling a recovery that's gone says the list changed: " + outcome.Gone.Message, outcome.Gone.Failed && outcome.Gone.Message == text.PasskeysChangedMessage);
            because.ItsTrue("and Back loads it again, with no notice: " + outcome.GoneBack, outcome.GoneBack == "1|False|False");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void CloseTheRecoveryPanelWhenAnotherActionStarts()
    {
        After.Setup(reg => reg.Set(new ScriptedPasskeyClient()))
        .When<ScriptedPasskeyClient>("opens the recovery panel, then starts a rename; opens it again, then a removal; and has a waiting recovery hand the move panel's refusal back", (client, reg) =>
        {
            PasskeyManagementViewModel screen = Screen(client, new FakePasskeyNavigator());
            NextList(client, Phone, Laptop, Tablet);
            Wait(screen.LoadAsync());

            Wait(screen.Passkeys[1].LostCommand.ExecuteAsync());
            Wait(screen.Passkeys[0].RenameCommand.ExecuteAsync());
            string afterRename = screen.ShowsRecovery + "|" + screen.HeirChoices.Count + "|" + screen.StartRecoveryCommand.CanExecute(null);

            Wait(screen.Passkeys[1].LostCommand.ExecuteAsync());
            bool editorClosed = screen.Editing is null;
            Wait(screen.Passkeys[2].RemoveCommand.ExecuteAsync());
            string afterRemove = screen.ShowsRecovery + "|" + screen.HeirChoices.Count;
            Wait(screen.KeepCommand.ExecuteAsync());

            Wait(screen.Passkeys[1].RemoveCommand.ExecuteAsync());
            client.DeleteResults.Enqueue(PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "KeySetHandoverRequired")));
            Wait(screen.ConfirmRemoveCommand.ExecuteAsync());
            screen.SelectedHeir = screen.HeirChoices[0];
            client.HandoverResults.Enqueue(PasskeyResult<PasskeySignOut>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, "RecoveryPending")));
            Wait(screen.MoveAccountKeyCommand.ExecuteAsync());
            string moveRefused = screen.ErrorMessage;
            NextList(client, Phone, Laptop, Tablet);
            string moveBack = BackReloads(client, screen);
            return new ClosedOutcome(afterRename, editorClosed, afterRemove, moveRefused, moveBack);
        })
        .TheTest
        .ShouldPass<ClosedOutcome>((because, outcome) =>
        {
            because.ItsTrue("a rename closes the recovery panel: " + outcome.AfterRename, outcome.AfterRename == "False|0|False");
            because.ItsTrue("opening the panel closes the editor", outcome.EditorClosed);
            because.ItsTrue("so does a removal: " + outcome.AfterRemove, outcome.AfterRemove == "False|0");
            because.ItsTrue("a move refused because a recovery waits says so: " + outcome.MoveRefused, outcome.MoveRefused == new PasskeyText().RecoveryPendingMessage);
            because.ItsTrue("and Back loads the list, which shows it: " + outcome.MoveBack, outcome.MoveBack == "1|False");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    // Leaves a refusal with Back: how many lists that loaded, and whether the recovery panel shows.
    private static string BackReloads(ScriptedPasskeyClient client, PasskeyManagementViewModel screen)
    {
        int before = client.Calls.Count(call => call == "list");
        Wait(screen.BackCommand.ExecuteAsync());
        return (client.Calls.Count(call => call == "list") - before) + "|" + (screen.ShowsRecovery || screen.ShowsHandover);
    }

    // The scripted client repeats its last list, so the next load gets exactly this one.
    private static void NextList(ScriptedPasskeyClient client, params PasskeyCredential[] credentials)
    {
        client.ListResults.Clear();
        client.ListResults.Enqueue(ScriptedPasskeyClient.Listed(credentials));
    }

    private static PasskeyResult<PasskeyRecoveryStarted> Refused(string code)
    {
        return PasskeyResult<PasskeyRecoveryStarted>.Failure(ScriptedPasskeyClient.Error(PasskeyErrorCategory.InvalidOptions, code));
    }

    private static PasskeyManagementViewModel Screen(ScriptedPasskeyClient client, IPasskeyNavigator navigator)
    {
        return new PasskeyManagementViewModel(client, PasskeyClientFixture.ValidConfiguration(), navigator);
    }

    private static PanelShot Panel(PasskeyManagementViewModel screen)
    {
        return new PanelShot(screen.ShowsRecovery, screen.RecoveryRemoves?.CredentialId ?? "none", string.Join(",", screen.HeirChoices.Select(item => item.CredentialId)), screen.SelectedHeir?.CredentialId ?? "none",
            screen.StartRecoveryCommand.CanExecute(null), screen.RemoveLostWithoutMovingCommand.CanExecute(null), screen.ShowsAdd);
    }

    private static RefusalShot Refusal(PasskeyManagementViewModel screen)
    {
        return new RefusalShot(screen.HasFailed, screen.ErrorMessage, screen.OffersTryAgain, screen.OffersBack);
    }

    private static void Wait(Task task)
    {
        task.GetAwaiter().GetResult();
    }

    private sealed record PanelShot(bool Shows, string Removes, string Choices, string Selected, bool StartEnabled, bool KeepEnabled, bool Add);

    private sealed record RefusalShot(bool Failed, string Message, bool TryAgain, bool Back);

    private sealed record RecoverOutcome(string Offers, bool NoNotice, PanelShot Opened, string Sent, string Notice, string Blocked, bool PanelGone, string? CancelledId, bool NoticeAfter, string Calls);

    private sealed record OpenOutcome(bool LostOffered, string FromMove, string Closed, string Recoverable, PanelShot Unnamed, bool HiddenWhileOpen, string Sent, string? SignedOutTo, string Kept, bool PanelAfterKeep);

    private sealed record RefusedOutcome(RefusalShot TooNew, string TooNewBack, RefusalShot TooNewLater, RefusalShot Heir, string HeirBack, string Reloads, RefusalShot Gone, string GoneBack, string TooNewExpected);

    private sealed record ClosedOutcome(string AfterRename, bool EditorClosed, string AfterRemove, string MoveRefused, string MoveBack);
}
