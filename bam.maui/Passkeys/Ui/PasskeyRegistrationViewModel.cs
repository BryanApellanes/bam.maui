using Bam.Logging;

namespace Bam.Maui.Passkeys;

/// <summary>
/// The registration screen: what a passkey is and why to use one, the sign-up fields, and the create button.
/// Success is shown only after the backend has verified the new credential; the success screen's
/// <see cref="DoneCommand"/> then calls <see cref="IPasskeyNavigator.SignedInAsync"/>, and the screen is cleared for the next account.
/// <para>
/// When the backend created the account but couldn't write its profile (<see cref="PasskeySignIn.ProfileCreated"/>
/// false), the success screen also offers to save the name and email typed at sign-up (<see cref="SaveProfileCommand"/>).
/// Done stays available: the app then receives <c>ProfileCreated = false</c> and can ask later.
/// </para>
/// </summary>
public sealed class PasskeyRegistrationViewModel : PasskeyFlowViewModel
{
    private string _handle = string.Empty;
    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private string _inviteCode = string.Empty;
    private PasskeySignIn? _signIn;

    /// <summary>Creates the view model.</summary>
    /// <param name="client">The passkey client.</param>
    /// <param name="configuration">The app's configuration.</param>
    /// <param name="navigator">The app's navigation.</param>
    /// <param name="logger">Where a failure in the app's navigation is logged, or null.</param>
    public PasskeyRegistrationViewModel(IPasskeyClient client, PasskeyConfiguration configuration, IPasskeyNavigator navigator, ILogger? logger = null)
        : base(client, configuration, navigator, logger)
    {
        CreateCommand = new PasskeyCommand(CreateAsync, () => State != PasskeyViewState.Working && !string.IsNullOrWhiteSpace(Handle) && !string.IsNullOrWhiteSpace(DisplayName));
        DoneCommand = new PasskeyCommand(DoneAsync, () => _signIn is not null && State == PasskeyViewState.Succeeded);
        SaveProfileCommand = new PasskeyCommand(SaveProfileAsync, () => NeedsProfile && State == PasskeyViewState.Succeeded && !string.IsNullOrWhiteSpace(DisplayName));
    }

    /// <inheritdoc />
    public override PasskeyFlow Flow => PasskeyFlow.Registration;

    /// <summary>The heading, with the app name.</summary>
    public string Title => PasskeyText.WithName(Text.RegistrationTitle, ApplicationName);

    /// <summary>What a passkey is.</summary>
    public string Explanation => Text.PasskeyExplanation;

    /// <summary>Why to use one, a line each.</summary>
    public IReadOnlyList<string> Benefits => Text.PasskeyBenefits;

    /// <summary>The success heading, with the app name.</summary>
    public string SuccessTitle => PasskeyText.WithName(Text.RegisteredTitle, ApplicationName);

    /// <summary>The line under the success heading.</summary>
    public string SuccessDetail => Text.RegisteredDetail;

    /// <summary>Whether the account was created without its profile, and saving it is still offered.</summary>
    public bool NeedsProfile => _signIn is { ProfileCreated: false };

    /// <summary>Whether the success screen shows the profile panel.</summary>
    public bool ShowsProfileStep => HasSucceeded && NeedsProfile;

    /// <summary>The profile panel's line.</summary>
    public string ProfileMessage => Text.ProfileNotSavedMessage;

    /// <summary>Whether the email field shows.</summary>
    public bool AsksForEmail => Configuration.Experience.AskForEmail;

    /// <summary>Whether the invite code field shows.</summary>
    public bool AsksForInviteCode => Configuration.Experience.AskForInviteCode;

    /// <summary>Whether the link to sign-in shows.</summary>
    public bool OffersSignIn => Configuration.Features.EnableAuthentication;

    /// <summary>The username to create.</summary>
    public string Handle
    {
        get => _handle;
        set
        {
            if (Set(ref _handle, value ?? string.Empty))
            {
                CreateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The name to show for the account.</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (Set(ref _displayName, value ?? string.Empty))
            {
                CreateCommand.RaiseCanExecuteChanged();
                SaveProfileCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The email address, when <see cref="AsksForEmail"/>.</summary>
    public string Email
    {
        get => _email;
        set => Set(ref _email, value ?? string.Empty);
    }

    /// <summary>The invite code, when <see cref="AsksForInviteCode"/>.</summary>
    public string InviteCode
    {
        get => _inviteCode;
        set => Set(ref _inviteCode, value ?? string.Empty);
    }

    /// <summary>Creates the account and its first passkey.</summary>
    public PasskeyCommand CreateCommand { get; }

    /// <summary>Leaves the success screen for the signed-in app.</summary>
    public PasskeyCommand DoneCommand { get; }

    /// <summary>Saves the name and email (as typed at sign-up, or changed on the success screen) when sign-up couldn't.</summary>
    public PasskeyCommand SaveProfileCommand { get; }

    /// <summary>Back from a failed "done" is the success screen again: the account exists.</summary>
    protected override PasskeyViewState DismissedState => _signIn is null ? PasskeyViewState.Ready : PasskeyViewState.Succeeded;

    /// <inheritdoc />
    protected override void RaiseCommands()
    {
        base.RaiseCommands();
        CreateCommand?.RaiseCanExecuteChanged();
        DoneCommand?.RaiseCanExecuteChanged();
        SaveProfileCommand?.RaiseCanExecuteChanged();
    }

    /// <inheritdoc />
    protected override void OnStateChanged()
    {
        OnPropertyChanged(nameof(ShowsProfileStep));
    }

    private Task CreateAsync()
    {
        _signIn = null;
        PasskeyRegistrationRequest request = new PasskeyRegistrationRequest
        {
            Handle = Handle.Trim(),
            DisplayName = DisplayName.Trim(),
            Email = AsksForEmail ? NullIfBlank(Email) : null,
            InviteCode = AsksForInviteCode ? NullIfBlank(InviteCode) : null
        };
        return RunAsync(token => Client.RegisterPasskeyAsync(request, token), signIn =>
        {
            SetSignIn(signIn);
            return Task.CompletedTask;
        }, typedInput: true);
    }

    private Task SaveProfileAsync()
    {
        PasskeySignIn? signIn = _signIn;
        if (signIn is null || signIn.ProfileCreated)
        {
            return Task.CompletedTask;
        }
        PasskeyProfile profile = new PasskeyProfile
        {
            DisplayName = DisplayName.Trim(),
            Email = AsksForEmail ? NullIfBlank(Email) : null
        };
        // A refusal (an email the host requires) goes Back to the success screen with the fields kept.
        return RunAsync(async token =>
        {
            PasskeyResult saved = await Client.SaveProfileAsync(profile, token);
            return saved.Error is null ? PasskeyResult<bool>.Success(true) : PasskeyResult<bool>.Failure(saved.Error);
        }, _ =>
        {
            SetSignIn(signIn with { ProfileCreated = true, DisplayName = profile.DisplayName });
            return Task.CompletedTask;
        }, typedInput: true, flow: PasskeyFlow.Profile, workingText: Text.Updating);
    }

    private void SetSignIn(PasskeySignIn? signIn)
    {
        _signIn = signIn;
        OnPropertyChanged(nameof(NeedsProfile));
        OnPropertyChanged(nameof(ShowsProfileStep));
        RaiseCommands();
    }

    private Task DoneAsync()
    {
        PasskeySignIn? signIn = _signIn;
        if (signIn is null)
        {
            return Task.CompletedTask;
        }
        return NavigateAsync(async () =>
        {
            await Navigator.SignedInAsync(signIn, PasskeyFlow.Registration);
            Clear();
        });
    }

    // The account is handed over: nothing of it stays on a screen the app may show again.
    private void Clear()
    {
        SetSignIn(null);
        Handle = string.Empty;
        DisplayName = string.Empty;
        Email = string.Empty;
        InviteCode = string.Empty;
        Reset();
    }

    private static string? NullIfBlank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
