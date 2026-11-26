using System.Collections.ObjectModel;
using System.Globalization;
using Bam.Logging;

namespace Bam.Maui.Passkeys;

/// <summary>
/// The passkey management screen: the signed-in account's passkeys, adding another (the system prompt creates it),
/// renaming one, removing one after asking, and signing out.  An account with no passkeys sees the empty state.  A
/// list that couldn't load is never shown as empty: the failure takes its place, with "try again".
/// <para>
/// Removing a passkey or signing out that ends the session calls <see cref="IPasskeyNavigator.SignedOutAsync"/> and
/// clears the screen.  A session that has already ended (<see cref="PasskeyErrorCategory.NotSignedIn"/>) offers
/// "sign in again".
/// </para>
/// <para>
/// The passkey that holds the account key (the key set) is tagged.  When the backend won't remove it until the key has
/// moved, the move panel opens: the person picks another passkey to hold the key and confirms with the one being removed
/// (<see cref="IPasskeyClient.HandOverKeySetAsync"/>), or removes it without moving the key.
/// </para>
/// <para>
/// For a lost device, "This device is lost" (on the account-key row and in the move panel) opens the recovery panel: the
/// person picks a passkey on this device to take over the key and confirms with it (<see cref="IPasskeyClient.RecoverKeySetAsync"/>).
/// The lost passkey stops signing in at once and the key moves after the backend's waiting period.  When no passkey holds
/// the key (<see cref="PasskeyList.KeySetRecoverable"/>) the screen offers the same recovery on its own.  A waiting recovery
/// shows a notice with its date and a Cancel, on every device the person is signed in on.
/// </para>
/// </summary>
public sealed class PasskeyManagementViewModel : PasskeyFlowViewModel
{
    private bool _loaded;
    private string _newPasskeyName = string.Empty;
    private PasskeyItem? _editing;
    private string _editName = string.Empty;
    private PasskeyItem? _pendingRemoval;
    private bool _signingOut;
    private PasskeyItem? _pendingHandover;
    private PasskeyItem? _selectedHeir;
    private bool _reloadOnBack;
    private PasskeyPendingRecovery? _pendingRecovery;
    private bool _keySetRecoverable;
    private bool _recoveryOpen;
    private PasskeyItem? _recoveryRemoves;

    /// <summary>Creates the view model.</summary>
    /// <param name="client">The passkey client.</param>
    /// <param name="configuration">The app's configuration.</param>
    /// <param name="navigator">The app's navigation.</param>
    /// <param name="logger">Where a failure in the app's navigation is logged, or null.</param>
    public PasskeyManagementViewModel(IPasskeyClient client, PasskeyConfiguration configuration, IPasskeyNavigator navigator, ILogger? logger = null)
        : base(client, configuration, navigator, logger)
    {
        AddCommand = new PasskeyCommand(AddAsync, () => IsReady && _loaded);
        RefreshCommand = new PasskeyCommand(LoadAsync, () => State != PasskeyViewState.Working);
        SignOutCommand = new PasskeyCommand(SignOutAsync, () => State != PasskeyViewState.Working);
        SaveNameCommand = new PasskeyCommand(SaveNameAsync, () => IsReady && _editing is not null);
        CancelEditCommand = new PasskeyCommand(CancelEditAsync, () => _editing is not null);
        ConfirmRemoveCommand = new PasskeyCommand(ConfirmRemoveAsync, () => IsReady && _pendingRemoval is not null && _pendingRemoval.CanRemove);
        KeepCommand = new PasskeyCommand(KeepAsync, () => _pendingRemoval is not null);
        MoveAccountKeyCommand = new PasskeyCommand(MoveAccountKeyAsync, () => IsReady && _pendingHandover is not null && _selectedHeir is not null);
        RemoveWithoutMovingCommand = new PasskeyCommand(RemoveWithoutMovingAsync, () => IsReady && _pendingHandover is not null);
        CancelHandoverCommand = new PasskeyCommand(CancelHandoverAsync, () => _pendingHandover is not null);
        LostDeviceCommand = new PasskeyCommand(LostDeviceAsync, () => IsReady && _pendingHandover is not null && _pendingRecovery is null);
        OpenRecoveryCommand = new PasskeyCommand(() => OpenRecoveryAsync(null), () => IsReady && _loaded && _keySetRecoverable && _pendingRecovery is null);
        StartRecoveryCommand = new PasskeyCommand(StartRecoveryAsync, () => IsReady && _recoveryOpen && _selectedHeir is not null);
        RemoveLostWithoutMovingCommand = new PasskeyCommand(RemoveLostWithoutMovingAsync, () => IsReady && _recoveryOpen && _recoveryRemoves is not null);
        CloseRecoveryCommand = new PasskeyCommand(CloseRecoveryAsync, () => _recoveryOpen);
        CancelRecoveryCommand = new PasskeyCommand(CancelRecoveryAsync, () => IsReady && _pendingRecovery is not null);
    }

    /// <inheritdoc />
    public override PasskeyFlow Flow => PasskeyFlow.Management;

    /// <summary>The heading.</summary>
    public string Title => Text.ManagementTitle;

    /// <summary>The line under the heading, with the app name.</summary>
    public string Intro => PasskeyText.WithName(Text.ManagementIntro, ApplicationName);

    /// <summary>The account's passkeys, oldest first, as the backend lists them.  Empty until the list has loaded.</summary>
    public ObservableCollection<PasskeyItem> Passkeys { get; } = new ObservableCollection<PasskeyItem>();

    /// <summary>Whether the list has loaded since the screen last appeared or changed.</summary>
    public bool IsLoaded
    {
        get => _loaded;
        private set
        {
            if (Set(ref _loaded, value))
            {
                RaiseShows();
            }
        }
    }

    /// <summary>Whether the list shows: loaded, with at least one passkey, and the screen waits for the user.</summary>
    public bool ShowsList => IsReady && IsLoaded && Passkeys.Count > 0;

    /// <summary>Whether the empty state shows: loaded, with no passkeys.</summary>
    public bool ShowsEmptyState => IsReady && IsLoaded && Passkeys.Count == 0;

    /// <summary>Whether adding a passkey is offered: the list has loaded and nothing else is open.</summary>
    public bool ShowsAdd => IsReady && IsLoaded && _editing is null && _pendingRemoval is null && _pendingHandover is null && !_recoveryOpen;

    /// <summary>The name to give the next passkey added, or blank for none.</summary>
    public string NewPasskeyName
    {
        get => _newPasskeyName;
        set => Set(ref _newPasskeyName, value ?? string.Empty);
    }

    /// <summary>The passkey being renamed, or null.</summary>
    public PasskeyItem? Editing
    {
        get => _editing;
        private set
        {
            if (Set(ref _editing, value))
            {
                RaiseShows();
                RaiseCommands();
            }
        }
    }

    /// <summary>Whether the rename panel shows.</summary>
    public bool ShowsEditor => IsReady && _editing is not null;

    /// <summary>The new name typed in the rename panel; blank clears the name.</summary>
    public string EditName
    {
        get => _editName;
        set => Set(ref _editName, value ?? string.Empty);
    }

    /// <summary>The passkey the removal confirmation asks about, or null.</summary>
    public PasskeyItem? PendingRemoval
    {
        get => _pendingRemoval;
        private set
        {
            if (Set(ref _pendingRemoval, value))
            {
                OnPropertyChanged(nameof(RemovalWarning));
                OnPropertyChanged(nameof(HasRemovalWarning));
                OnPropertyChanged(nameof(RemovalAccountKeyNote));
                OnPropertyChanged(nameof(HasRemovalAccountKeyNote));
                RaiseShows();
                RaiseCommands();
            }
        }
    }

    /// <summary>Whether the removal confirmation shows.</summary>
    public bool ShowsRemoval => IsReady && _pendingRemoval is not null;

    /// <summary>The removal confirmation's extra line for the passkey this session was opened with, or empty.</summary>
    public string RemovalWarning => _pendingRemoval is { IsCurrent: true } ? Text.RemoveCurrentMessage : string.Empty;

    /// <summary>Whether <see cref="RemovalWarning"/> has text.</summary>
    public bool HasRemovalWarning => RemovalWarning.Length > 0;

    /// <summary>The removal confirmation's extra line for the passkey that holds the account key, or empty.</summary>
    public string RemovalAccountKeyNote => _pendingRemoval is { IsAccountKey: true } ? Text.RemoveAccountKeyMessage : string.Empty;

    /// <summary>Whether <see cref="RemovalAccountKeyNote"/> has text.</summary>
    public bool HasRemovalAccountKeyNote => RemovalAccountKeyNote.Length > 0;

    /// <summary>
    /// The passkey whose account key the move panel asks about, or null.  The panel opens when the backend refuses to
    /// remove the passkey that holds the account key until the key has moved (bam.useraccounts#20).
    /// </summary>
    public PasskeyItem? PendingHandover
    {
        get => _pendingHandover;
        private set
        {
            if (Set(ref _pendingHandover, value))
            {
                RaiseShows();
                RaiseCommands();
            }
        }
    }

    /// <summary>Whether the move panel shows.</summary>
    public bool ShowsHandover => IsReady && _pendingHandover is not null;

    /// <summary>The passkeys the account key can move to, in the move panel or the recovery panel: every listed one except the one being removed.</summary>
    public ObservableCollection<PasskeyItem> HeirChoices { get; } = new ObservableCollection<PasskeyItem>();

    /// <summary>
    /// The passkey the account key moves to.  It starts as the one this session signed in with when that one stays, or
    /// the only other passkey; otherwise it starts empty and Move is off until the person chooses.
    /// </summary>
    public PasskeyItem? SelectedHeir
    {
        get => _selectedHeir;
        set
        {
            if (Set(ref _selectedHeir, value))
            {
                MoveAccountKeyCommand?.RaiseCanExecuteChanged();
                StartRecoveryCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Moves the account key to <see cref="SelectedHeir"/> and removes the passkey: the system prompt asks for the passkey being removed.</summary>
    public PasskeyCommand MoveAccountKeyCommand { get; }

    /// <summary>Removes the passkey without moving the account key: its key still counts as the account key until it is replaced.</summary>
    public PasskeyCommand RemoveWithoutMovingCommand { get; }

    /// <summary>Closes the move panel without removing anything.</summary>
    public PasskeyCommand CancelHandoverCommand { get; }

    /// <summary>From the move panel: the passkey's device is lost, so open the recovery panel for it instead.</summary>
    public PasskeyCommand LostDeviceCommand { get; }

    /// <summary>Opens the recovery panel when no passkey holds the account key (<see cref="ShowsRecoverable"/>).</summary>
    public PasskeyCommand OpenRecoveryCommand { get; }

    /// <summary>Starts the recovery to <see cref="SelectedHeir"/>: the system prompt asks for that passkey.</summary>
    public PasskeyCommand StartRecoveryCommand { get; }

    /// <summary>From the recovery panel: removes the lost passkey without moving the account key.</summary>
    public PasskeyCommand RemoveLostWithoutMovingCommand { get; }

    /// <summary>Closes the recovery panel without starting anything.</summary>
    public PasskeyCommand CloseRecoveryCommand { get; }

    /// <summary>Cancels the waiting recovery (<see cref="PendingRecovery"/>).</summary>
    public PasskeyCommand CancelRecoveryCommand { get; }

    /// <summary>The waiting lost-device recovery the backend reported, or null.</summary>
    public PasskeyPendingRecovery? PendingRecovery => _pendingRecovery;

    /// <summary>Whether the notice about a waiting recovery shows.</summary>
    public bool ShowsPendingRecovery => IsReady && IsLoaded && _pendingRecovery is not null;

    /// <summary>The notice about a waiting recovery: the passkey the key moves to and when, or empty.</summary>
    public string PendingRecoveryText
    {
        get
        {
            if (_pendingRecovery is not { } pending)
            {
                return string.Empty;
            }
            string heir = Passkeys.FirstOrDefault(item => string.Equals(item.CredentialId, pending.HeirCredentialId, StringComparison.Ordinal))?.Title ?? Text.UnlistedHeir;
            // {1} first: the date has no braces, and the passkey's name, which the person chose, is never scanned again.
            return PasskeyText.WithName(Text.PendingRecoveryMessage.Replace("{1}", MomentText(pending.EffectiveUtc), StringComparison.Ordinal), heir);
        }
    }

    /// <summary>Whether the screen offers a recovery because no passkey holds the account key.</summary>
    public bool ShowsRecoverable => IsReady && IsLoaded && _keySetRecoverable && _pendingRecovery is null && !_recoveryOpen && _pendingHandover is null;

    /// <summary>Whether the recovery panel shows.</summary>
    public bool ShowsRecovery => IsReady && _recoveryOpen;

    /// <summary>The lost passkey the recovery panel takes out of sign-in, or null when no passkey holds the key.</summary>
    public PasskeyItem? RecoveryRemoves => _recoveryRemoves;

    /// <summary>Whether the recovery panel names a lost passkey (and so offers removing it without moving the key).</summary>
    public bool HasRecoveryRemoves => _recoveryRemoves is not null;

    /// <summary>Adds a passkey: the system prompt creates it, then the list loads again.</summary>
    public PasskeyCommand AddCommand { get; }

    /// <summary>Loads the list again.</summary>
    public PasskeyCommand RefreshCommand { get; }

    /// <summary>Ends the session.</summary>
    public PasskeyCommand SignOutCommand { get; }

    /// <summary>Saves the rename panel's name.</summary>
    public PasskeyCommand SaveNameCommand { get; }

    /// <summary>Closes the rename panel without renaming.</summary>
    public PasskeyCommand CancelEditCommand { get; }

    /// <summary>Removes the passkey the confirmation asks about.</summary>
    public PasskeyCommand ConfirmRemoveCommand { get; }

    /// <summary>Closes the removal confirmation without removing.</summary>
    public PasskeyCommand KeepCommand { get; }

    /// <summary>
    /// Lists the passkeys.  Call it when the screen appears.  A list that couldn't load takes the place of the screen,
    /// with no Back (an empty list there would be a lie) and a "try again" that loads again.
    /// </summary>
    /// <returns>A task that completes when the list has loaded or failed.</returns>
    public Task LoadAsync()
    {
        return RunAsync(token => Client.GetPasskeyListAsync(token), ShowAsync, workingText: Text.Updating, replacesForm: true);
    }

    /// <summary>Whether the signed-out panel shows: the session has ended and the app's <see cref="IPasskeyNavigator.SignedOutAsync"/> is running.</summary>
    public bool ShowsSigningOut => _signingOut && HasSucceeded;

    /// <summary>Shown while the app's <see cref="IPasskeyNavigator.SignedOutAsync"/> runs.</summary>
    public string SignedOutTitle => Text.SignedOutTitle;

    /// <summary>The line under <see cref="SignedOutTitle"/>.</summary>
    public string SigningOutText => Text.SigningOut;

    /// <inheritdoc />
    protected override void OnStateChanged()
    {
        // A session found gone takes the account off the screen, so Back doesn't bring back a list nothing can act on.
        // It is keyed on the error, not on the plan's buttons: an app that doesn't sign in with passkeys gets "try again"
        // instead of "sign in again" for the same lost session.
        if (State == PasskeyViewState.Failed && LastError is { Category: PasskeyErrorCategory.NotSignedIn })
        {
            ClearAccount();
        }
        RaiseShows();
    }

    /// <inheritdoc />
    protected override void RaiseCommands()
    {
        base.RaiseCommands();
        AddCommand?.RaiseCanExecuteChanged();
        RefreshCommand?.RaiseCanExecuteChanged();
        SignOutCommand?.RaiseCanExecuteChanged();
        SaveNameCommand?.RaiseCanExecuteChanged();
        CancelEditCommand?.RaiseCanExecuteChanged();
        ConfirmRemoveCommand?.RaiseCanExecuteChanged();
        KeepCommand?.RaiseCanExecuteChanged();
        MoveAccountKeyCommand?.RaiseCanExecuteChanged();
        RemoveWithoutMovingCommand?.RaiseCanExecuteChanged();
        CancelHandoverCommand?.RaiseCanExecuteChanged();
        LostDeviceCommand?.RaiseCanExecuteChanged();
        OpenRecoveryCommand?.RaiseCanExecuteChanged();
        StartRecoveryCommand?.RaiseCanExecuteChanged();
        RemoveLostWithoutMovingCommand?.RaiseCanExecuteChanged();
        CloseRecoveryCommand?.RaiseCanExecuteChanged();
        CancelRecoveryCommand?.RaiseCanExecuteChanged();
        foreach (PasskeyItem item in Passkeys)
        {
            item.RenameCommand.RaiseCanExecuteChanged();
            item.RemoveCommand.RaiseCanExecuteChanged();
            item.LostCommand.RaiseCanExecuteChanged();
        }
    }

    private Task ShowAsync(PasskeyList list)
    {
        RestoreForm();
        Passkeys.Clear();
        IReadOnlyList<PasskeyCredential> credentials = list.Passkeys;
        bool canRemove = credentials.Count > 1;
        foreach (PasskeyCredential credential in credentials)
        {
            Passkeys.Add(new PasskeyItem(credential, Text, canRemove, BeginRename, BeginRemoval, BeginLost, () => IsReady, () => _pendingRecovery is null));
        }
        Editing = null;
        PendingRemoval = null;
        CloseHandover();
        CloseRecovery();
        _pendingRecovery = list.PendingRecovery;
        _keySetRecoverable = list.KeySetRecoverable;
        OnPropertyChanged(nameof(PendingRecovery));
        OnPropertyChanged(nameof(PendingRecoveryText));
        _reloadOnBack = false;
        IsLoaded = true;
        Reset();
        RaiseShows();
        return Task.CompletedTask;
    }

    private Task AddAsync()
    {
        string? name = string.IsNullOrWhiteSpace(NewPasskeyName) ? null : NewPasskeyName.Trim();
        return RunAsync(token => Client.AddPasskeyAsync(name, token), _ =>
        {
            NewPasskeyName = string.Empty;
            return LoadAsync();
        });
    }

    private Task SaveNameAsync()
    {
        PasskeyItem? item = _editing;
        if (item is null)
        {
            return Task.CompletedTask;
        }
        string? name = string.IsNullOrWhiteSpace(EditName) ? null : EditName.Trim();
        // The panel stays open until the rename works, so a refused name can be changed after Back.
        return RunAsync(async token => Typed(await Client.RenamePasskeyAsync(item.CredentialId, name, token)), _ => LoadAsync(), typedInput: true, workingText: Text.Updating);
    }

    private Task ConfirmRemoveAsync()
    {
        PasskeyItem? item = _pendingRemoval;
        if (item is null)
        {
            return Task.CompletedTask;
        }
        PendingRemoval = null;
        return RunAsync(async token => Removal(await Client.DeletePasskeyAsync(item.CredentialId, PasskeyKeySetRemoval.Default, token)), removal =>
        {
            return removal.SignOut is null ? OpenHandoverAsync(item) : RemovedAsync(removal.SignOut);
        }, typedInput: true, workingText: Text.Updating);
    }

    private Task RemovedAsync(PasskeySignOut signOut)
    {
        return signOut.SignedOut ? SignedOutAsync(signOut) : LoadAsync();
    }

    // The backend asked for the account key to move first: a refusal that opens the move panel, not an error screen.
    private PasskeyResult<RemovalOutcome> Removal(PasskeyResult<PasskeySignOut> result)
    {
        if (result.Error is { BackendCode: HttpPasskeyBackend.BackendCodes.KeySetHandoverRequired })
        {
            return PasskeyResult<RemovalOutcome>.Success(new RemovalOutcome(null));
        }
        PasskeyResult<PasskeySignOut> worded = WithRemovalWording(result);
        return worded.Succeeded ? PasskeyResult<RemovalOutcome>.Success(new RemovalOutcome(worded.Value!)) : PasskeyResult<RemovalOutcome>.Failure(worded.Error!);
    }

    private Task OpenHandoverAsync(PasskeyItem item)
    {
        Editing = null;
        CloseRecovery();
        FillHeirs(item);
        PendingHandover = item;
        Reset();
        RaiseShows();
        return Task.CompletedTask;
    }

    // Every listed passkey except the one going.  This session's passkey when it stays, or the only other one; otherwise
    // the person chooses, and Move (or Recover) stays off until they do, rather than the key going to whichever passkey
    // happens to be listed first.
    private void FillHeirs(PasskeyItem? except)
    {
        HeirChoices.Clear();
        foreach (PasskeyItem passkey in Passkeys)
        {
            if (except is null || !string.Equals(passkey.CredentialId, except.CredentialId, StringComparison.Ordinal))
            {
                HeirChoices.Add(passkey);
            }
        }
        SelectedHeir = HeirChoices.FirstOrDefault(passkey => passkey.IsCurrent) ?? (HeirChoices.Count == 1 ? HeirChoices[0] : null);
    }

    private void BeginLost(PasskeyItem item)
    {
        OpenRecovery(item);
    }

    private Task LostDeviceAsync()
    {
        PasskeyItem? item = _pendingHandover;
        if (item is not null)
        {
            OpenRecovery(item);
        }
        return Task.CompletedTask;
    }

    private Task OpenRecoveryAsync(PasskeyItem? removes)
    {
        OpenRecovery(removes);
        return Task.CompletedTask;
    }

    private void OpenRecovery(PasskeyItem? removes)
    {
        Editing = null;
        PendingRemoval = null;
        CloseHandover();
        FillHeirs(removes);
        _recoveryRemoves = removes;
        _recoveryOpen = true;
        OnPropertyChanged(nameof(RecoveryRemoves));
        OnPropertyChanged(nameof(HasRecoveryRemoves));
        Reset();
        RaiseShows();
        RaiseCommands();
    }

    // The heir is always named: the backend's default is this session's passkey, which may be the lost one.  The panel
    // stays open until the recovery starts, so a heir that is too new can be swapped after Back.
    private Task StartRecoveryAsync()
    {
        PasskeyItem? heir = _selectedHeir;
        if (!_recoveryOpen || heir is null)
        {
            return Task.CompletedTask;
        }
        string? removes = _recoveryRemoves?.CredentialId;
        return RunAsync(async token => WithRecoveryWording(await Client.RecoverKeySetAsync(heir.CredentialId, removes, token)),
            started => started.SignOut.SignedOut ? SignedOutAsync(started.SignOut) : LoadAsync(), typedInput: true);
    }

    private Task RemoveLostWithoutMovingAsync()
    {
        PasskeyItem? item = _recoveryRemoves;
        if (!_recoveryOpen || item is null)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async token => WithKeepWording(await Client.DeletePasskeyAsync(item.CredentialId, PasskeyKeySetRemoval.KeepKeySet, token)), RemovedAsync, typedInput: true, workingText: Text.Updating);
    }

    private Task CloseRecoveryAsync()
    {
        CloseRecovery();
        return Task.CompletedTask;
    }

    private void CloseRecovery()
    {
        if (!_recoveryOpen && _recoveryRemoves is null)
        {
            return;
        }
        _recoveryOpen = false;
        _recoveryRemoves = null;
        SelectedHeir = null;
        HeirChoices.Clear();
        OnPropertyChanged(nameof(RecoveryRemoves));
        OnPropertyChanged(nameof(HasRecoveryRemoves));
        RaiseShows();
        RaiseCommands();
    }

    // Cancelling a recovery someone else started is the point of the notice, so it asks nothing first.  A recovery that
    // is already gone (it moved, or another device cancelled it) loads the list again on Back.
    private Task CancelRecoveryAsync()
    {
        PasskeyPendingRecovery? pending = _pendingRecovery;
        if (pending is null)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async token =>
        {
            PasskeyResult<bool> cancelled = Typed(await Client.CancelKeySetRecoveryAsync(pending.RecoveryId, token));
            return cancelled.Error is { BackendCode: HttpPasskeyBackend.BackendCodes.NotFound } ? Reworded(cancelled, Text.PasskeysChangedMessage, true) : cancelled;
        }, _ => LoadAsync(), typedInput: true, workingText: Text.Updating);
    }

    // A refused recovery says why in the app's words.  A refused or too-new heir goes back to the panel to choose another;
    // a recovery that can't start because things changed (or can't start here at all) loads the list again on Back.
    private PasskeyResult<PasskeyRecoveryStarted> WithRecoveryWording(PasskeyResult<PasskeyRecoveryStarted> result)
    {
        if (result.Error is not { BackendCode: not null } error)
        {
            return result;
        }
        switch (error.BackendCode)
        {
            case HttpPasskeyBackend.BackendCodes.InvalidHeir:
                return Reworded(result, Text.AccountKeyHeirRefusedMessage, false);
            case HttpPasskeyBackend.BackendCodes.HeirTooNew:
                return Reworded(result, error.EligibleUtc is DateTime eligible ? PasskeyText.WithName(Text.RecoveryHeirTooNewMessage, MomentText(eligible)) : Text.RecoveryHeirTooNewLaterMessage, false);
            case HttpPasskeyBackend.BackendCodes.RecoveryPending:
                return Reworded(result, Text.RecoveryPendingMessage, true);
            case HttpPasskeyBackend.BackendCodes.RotationUnavailable:
                return Reworded(result, Text.RecoveryUnavailableMessage, true);
            case HttpPasskeyBackend.BackendCodes.LastCredential:
            case HttpPasskeyBackend.BackendCodes.LastPasskey:
                return Reworded(result, Text.LastPasskeyMessage, true);
            case HttpPasskeyBackend.BackendCodes.NotFound:
            case HttpPasskeyBackend.BackendCodes.NotKeySet:
            case HttpPasskeyBackend.BackendCodes.RemovesRequired:
            case HttpPasskeyBackend.BackendCodes.NoKeySet:
            case HttpPasskeyBackend.BackendCodes.KeySetChanged:
            case HttpPasskeyBackend.BackendCodes.HeirGone:
                return Reworded(result, Text.PasskeysChangedMessage, true);
        }
        return result;
    }

    // The same failure in the app's words; reloadOnBack when it showed the list is out of date.
    private PasskeyResult<TValue> Reworded<TValue>(PasskeyResult<TValue> result, string message, bool reloadOnBack)
    {
        PasskeyError error = result.Error!;
        if (reloadOnBack)
        {
            _reloadOnBack = true;
        }
        return PasskeyResult<TValue>.Failure(new PasskeyError(error.Category, message, error.BackendCode, error.EligibleUtc));
    }

    private static string MomentText(DateTime utc)
    {
        DateTime asUtc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc;
        return asUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    // The heir is always named, even when it is this session's passkey: the backend's default would be the passkey being
    // removed when this session signed in with it.  The panel stays open until the move works, so Back returns to it.
    private Task MoveAccountKeyAsync()
    {
        PasskeyItem? item = _pendingHandover;
        PasskeyItem? heir = _selectedHeir;
        if (item is null || heir is null)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async token => WithHandoverWording(await Client.HandOverKeySetAsync(item.CredentialId, heir.CredentialId, token)), RemovedAsync, typedInput: true);
    }

    private Task RemoveWithoutMovingAsync()
    {
        PasskeyItem? item = _pendingHandover;
        if (item is null)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async token => WithKeepWording(await Client.DeletePasskeyAsync(item.CredentialId, PasskeyKeySetRemoval.KeepKeySet, token)), RemovedAsync, typedInput: true, workingText: Text.Updating);
    }

    // The panel opened because this passkey could be removed once the key moved, so a refusal now means the list changed
    // after it opened (a passkey removed elsewhere, or this one): Back goes to a fresh list, as for a refused move.
    private PasskeyResult<PasskeySignOut> WithKeepWording(PasskeyResult<PasskeySignOut> result)
    {
        if (result.Error is { BackendCode: HttpPasskeyBackend.BackendCodes.LastPasskey or HttpPasskeyBackend.BackendCodes.NotFound })
        {
            _reloadOnBack = true;
        }
        return WithRemovalWording(result);
    }

    private Task CancelHandoverAsync()
    {
        CloseHandover();
        return Task.CompletedTask;
    }

    private void CloseHandover()
    {
        PendingHandover = null;
        SelectedHeir = null;
        HeirChoices.Clear();
    }

    /// <inheritdoc />
    protected override Task AfterBackAsync()
    {
        if (!_reloadOnBack)
        {
            return Task.CompletedTask;
        }
        // The failure showed the list is out of date: Back goes to a fresh list, not to a panel about passkeys that changed.
        _reloadOnBack = false;
        CloseHandover();
        CloseRecovery();
        return LoadAsync();
    }

    // A refused handover says what changed in the app's words.  A refused heir goes back to the panel to choose another;
    // a list that changed underneath (a passkey removed elsewhere, the key set moved) is loaded again on Back.
    private PasskeyResult<PasskeySignOut> WithHandoverWording(PasskeyResult<PasskeySignOut> result)
    {
        if (result.Error is not { BackendCode: not null } error)
        {
            return result;
        }
        string? message = null;
        switch (error.BackendCode)
        {
            case HttpPasskeyBackend.BackendCodes.InvalidHeir:
                message = Text.AccountKeyHeirRefusedMessage;
                break;
            case HttpPasskeyBackend.BackendCodes.RecoveryPending:
                // A lost-device recovery is waiting; the list shows it, with its Cancel.
                message = Text.RecoveryPendingMessage;
                _reloadOnBack = true;
                break;
            case HttpPasskeyBackend.BackendCodes.LastCredential:
            case HttpPasskeyBackend.BackendCodes.LastPasskey:
                message = Text.LastPasskeyMessage;
                _reloadOnBack = true;
                break;
            case HttpPasskeyBackend.BackendCodes.NotFound:
            case HttpPasskeyBackend.BackendCodes.NotKeySet:
            case HttpPasskeyBackend.BackendCodes.RotationUnavailable:
            case HttpPasskeyBackend.BackendCodes.KeySetChanged:
            case HttpPasskeyBackend.BackendCodes.HeirGone:
                message = Text.PasskeysChangedMessage;
                _reloadOnBack = true;
                break;
        }
        return message is null ? result : PasskeyResult<PasskeySignOut>.Failure(new PasskeyError(error.Category, message, error.BackendCode));
    }

    // The person asked to leave, and the client forgets the session however the call ends (IPasskeyClient.SignOutAsync),
    // so a failed or cancelled sign-out still signs out here: the account leaves the screen and the app is told, with no
    // next URL because the server didn't give one.
    private Task SignOutAsync()
    {
        ClearAccount();
        return RunAsync(async token =>
        {
            PasskeyResult<PasskeySignOut> result = await Client.SignOutAsync(token);
            if (result.Succeeded)
            {
                return result;
            }
            Logger?.Warning("Passkey {0} screen: sign-out failed ({1}); signed out on this device.", Flow.ToString(), result.Error!.Category.ToString());
            return PasskeyResult<PasskeySignOut>.Success(new PasskeySignOut { SignedOut = true, NextUrl = null });
        }, SignedOutAsync, workingText: Text.Updating);
    }

    // The session is over: nothing of the account stays on a screen the app may show again, so it is cleared before the
    // app's navigation runs.  If that navigation throws, Back from its failure finds an empty screen, not the old list.
    // While it runs the screen is Succeeded and shows SignedOutTitle.
    private async Task SignedOutAsync(PasskeySignOut signOut)
    {
        ClearAccount();
        _signingOut = true;
        OnPropertyChanged(nameof(ShowsSigningOut));
        try
        {
            await Navigator.SignedOutAsync(signOut);
        }
        finally
        {
            _signingOut = false;
            OnPropertyChanged(nameof(ShowsSigningOut));
        }
        Reset();
    }

    private void ClearAccount()
    {
        Passkeys.Clear();
        NewPasskeyName = string.Empty;
        EditName = string.Empty;
        Editing = null;
        PendingRemoval = null;
        CloseHandover();
        CloseRecovery();
        _pendingRecovery = null;
        _keySetRecoverable = false;
        OnPropertyChanged(nameof(PendingRecovery));
        OnPropertyChanged(nameof(PendingRecoveryText));
        _reloadOnBack = false;
        IsLoaded = false;
        RaiseShows();
    }

    private void BeginRename(PasskeyItem item)
    {
        PendingRemoval = null;
        CloseHandover();
        CloseRecovery();
        EditName = item.Name ?? string.Empty;
        Editing = item;
    }

    private void BeginRemoval(PasskeyItem item)
    {
        Editing = null;
        CloseHandover();
        CloseRecovery();
        PendingRemoval = item;
    }

    private Task CancelEditAsync()
    {
        Editing = null;
        EditName = string.Empty;
        return Task.CompletedTask;
    }

    private Task KeepAsync()
    {
        PendingRemoval = null;
        return Task.CompletedTask;
    }

    // The server refuses to remove the last passkey with its own code; the screen says so in the app's words.
    private PasskeyResult<PasskeySignOut> WithRemovalWording(PasskeyResult<PasskeySignOut> result)
    {
        if (result.Error is { BackendCode: HttpPasskeyBackend.BackendCodes.LastPasskey } error)
        {
            return PasskeyResult<PasskeySignOut>.Failure(new PasskeyError(error.Category, Text.LastPasskeyMessage, error.BackendCode));
        }
        return result;
    }

    private static PasskeyResult<bool> Typed(PasskeyResult result)
    {
        return result.Error is null ? PasskeyResult<bool>.Success(true) : PasskeyResult<bool>.Failure(result.Error);
    }

    private void RaiseShows()
    {
        OnPropertyChanged(nameof(ShowsList));
        OnPropertyChanged(nameof(ShowsEmptyState));
        OnPropertyChanged(nameof(ShowsAdd));
        OnPropertyChanged(nameof(ShowsEditor));
        OnPropertyChanged(nameof(ShowsRemoval));
        OnPropertyChanged(nameof(ShowsHandover));
        OnPropertyChanged(nameof(ShowsPendingRecovery));
        OnPropertyChanged(nameof(ShowsRecoverable));
        OnPropertyChanged(nameof(ShowsRecovery));
        OnPropertyChanged(nameof(ShowsSigningOut));
    }

    // What a removal did: the sign-out it reported, or null when the backend asked for the account key to move first.
    private sealed record RemovalOutcome(PasskeySignOut? SignOut);
}

/// <summary>One row of the management list: what the backend reported about a passkey, as the screen shows it.</summary>
public sealed class PasskeyItem
{
    internal PasskeyItem(PasskeyCredential credential, PasskeyText text, bool canRemove, Action<PasskeyItem> rename, Action<PasskeyItem> remove, Action<PasskeyItem> lost, Func<bool> ready, Func<bool> canRecover)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(text);
        Credential = credential;
        CanRemove = canRemove;
        Title = string.IsNullOrWhiteSpace(credential.Name) ? text.UnnamedPasskey : credential.Name;
        AddedText = credential.CreatedUtc is DateTime created ? PasskeyText.WithName(text.AddedOn, DateText(created)) : string.Empty;
        LastUsedText = credential.LastUsedUtc is DateTime used ? PasskeyText.WithName(text.LastUsedOn, DateText(used)) : string.Empty;
        DeviceText = credential.Device ?? string.Empty;
        CurrentText = credential.IsCurrent == true ? text.CurrentPasskey : string.Empty;
        AccountKeyText = credential.IsRegisteredKeySet == true ? text.AccountKeyPasskey : string.Empty;
        RenameCommand = new PasskeyCommand(() =>
        {
            rename(this);
            return Task.CompletedTask;
        }, ready);
        RemoveCommand = new PasskeyCommand(() =>
        {
            remove(this);
            return Task.CompletedTask;
        }, () => CanRemove && ready());
        LostCommand = new PasskeyCommand(() =>
        {
            lost(this);
            return Task.CompletedTask;
        }, () => OffersLost && ready() && canRecover());
    }

    /// <summary>The passkey as the backend reported it.</summary>
    public PasskeyCredential Credential { get; }

    /// <summary>The credential id.</summary>
    public string CredentialId => Credential.CredentialId;

    /// <summary>The name the person gave it, or null.</summary>
    public string? Name => Credential.Name;

    /// <summary>The name, or <see cref="PasskeyText.UnnamedPasskey"/>.</summary>
    public string Title { get; }

    /// <summary>When it was added, in the device's culture, or empty when the backend didn't say.</summary>
    public string AddedText { get; }

    /// <summary>Whether <see cref="AddedText"/> has text.</summary>
    public bool HasAddedText => AddedText.Length > 0;

    /// <summary>When it last signed in, or empty when the backend didn't say.</summary>
    public string LastUsedText { get; }

    /// <summary>Whether <see cref="LastUsedText"/> has text.</summary>
    public bool HasLastUsedText => LastUsedText.Length > 0;

    /// <summary>The device or provider, or empty: shown only when the backend reported one, never guessed.</summary>
    public string DeviceText { get; }

    /// <summary>Whether <see cref="DeviceText"/> has text.</summary>
    public bool HasDeviceText => DeviceText.Length > 0;

    /// <summary>The tag on the passkey this session was opened with, or empty.</summary>
    public string CurrentText { get; }

    /// <summary>Whether the session was opened with this passkey.</summary>
    public bool IsCurrent => Credential.IsCurrent == true;

    /// <summary>The tag on the passkey that holds the account key (<see cref="PasskeyCredential.IsRegisteredKeySet"/>), or empty.</summary>
    public string AccountKeyText { get; }

    /// <summary>Whether this passkey holds the account key; shown only when the backend reported it.</summary>
    public bool IsAccountKey => Credential.IsRegisteredKeySet == true;

    /// <summary>Whether it can be removed: not when it is the account's only passkey.</summary>
    public bool CanRemove { get; }

    /// <summary>Opens the rename panel for this passkey.</summary>
    public PasskeyCommand RenameCommand { get; }

    /// <summary>Opens the removal confirmation for this passkey.</summary>
    public PasskeyCommand RemoveCommand { get; }

    /// <summary>Whether the row offers "This device is lost": it holds the account key and another passkey could take it over.</summary>
    public bool OffersLost => IsAccountKey && CanRemove;

    /// <summary>Opens the recovery panel for this passkey, whose device is lost.</summary>
    public PasskeyCommand LostCommand { get; }

    private static string DateText(DateTime utc)
    {
        DateTime asUtc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc;
        return asUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }
}
