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
/// </summary>
public sealed class PasskeyManagementViewModel : PasskeyFlowViewModel
{
    private bool _loaded;
    private string _newPasskeyName = string.Empty;
    private PasskeyItem? _editing;
    private string _editName = string.Empty;
    private PasskeyItem? _pendingRemoval;

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
    public bool ShowsAdd => IsReady && IsLoaded && _editing is null && _pendingRemoval is null;

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
        return RunAsync(token => Client.GetRegisteredPasskeysAsync(token), ShowAsync, workingText: Text.Updating, replacesForm: true);
    }

    /// <inheritdoc />
    protected override void OnStateChanged()
    {
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
        foreach (PasskeyItem item in Passkeys)
        {
            item.RenameCommand.RaiseCanExecuteChanged();
            item.RemoveCommand.RaiseCanExecuteChanged();
        }
    }

    private Task ShowAsync(IReadOnlyList<PasskeyCredential> credentials)
    {
        RestoreForm();
        Passkeys.Clear();
        bool canRemove = credentials.Count > 1;
        foreach (PasskeyCredential credential in credentials)
        {
            Passkeys.Add(new PasskeyItem(credential, Text, canRemove, BeginRename, BeginRemoval, () => IsReady));
        }
        Editing = null;
        PendingRemoval = null;
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
        return RunAsync(async token => WithRemovalWording(await Client.DeletePasskeyAsync(item.CredentialId, token)), RemovedAsync, typedInput: true, workingText: Text.Updating);
    }

    private Task RemovedAsync(PasskeySignOut signOut)
    {
        return signOut.SignedOut ? SignedOutAsync(signOut) : LoadAsync();
    }

    private Task SignOutAsync()
    {
        Editing = null;
        PendingRemoval = null;
        return RunAsync(token => Client.SignOutAsync(token), SignedOutAsync, workingText: Text.Updating);
    }

    // The session is over: nothing of the account stays on a screen the app may show again.
    private async Task SignedOutAsync(PasskeySignOut signOut)
    {
        await Navigator.SignedOutAsync(signOut);
        Passkeys.Clear();
        NewPasskeyName = string.Empty;
        EditName = string.Empty;
        Editing = null;
        PendingRemoval = null;
        IsLoaded = false;
        Reset();
    }

    private void BeginRename(PasskeyItem item)
    {
        PendingRemoval = null;
        EditName = item.Name ?? string.Empty;
        Editing = item;
    }

    private void BeginRemoval(PasskeyItem item)
    {
        Editing = null;
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
    }
}

/// <summary>One row of the management list: what the backend reported about a passkey, as the screen shows it.</summary>
public sealed class PasskeyItem
{
    internal PasskeyItem(PasskeyCredential credential, PasskeyText text, bool canRemove, Action<PasskeyItem> rename, Action<PasskeyItem> remove, Func<bool> ready)
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

    /// <summary>Whether it can be removed: not when it is the account's only passkey.</summary>
    public bool CanRemove { get; }

    /// <summary>Opens the rename panel for this passkey.</summary>
    public PasskeyCommand RenameCommand { get; }

    /// <summary>Opens the removal confirmation for this passkey.</summary>
    public PasskeyCommand RemoveCommand { get; }

    private static string DateText(DateTime utc)
    {
        DateTime asUtc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc;
        return asUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }
}
