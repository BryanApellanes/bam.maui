using Bam.Logging;

namespace Bam.Maui.Passkeys;

/// <summary>
/// The sign-in screen: the passkey button, the username field the <see cref="PasskeySignInMode"/> asks for,
/// the app's other way to sign in, and the link to registration.  A verified sign-in goes straight to
/// <see cref="IPasskeyNavigator.SignedInAsync"/>; while that runs the screen shows <see cref="SignedInTitle"/> and
/// <see cref="SigningInText"/>, never a blank page.
/// <para>
/// A sign-in that reports a waiting key-set recovery (<see cref="PasskeySignIn.PendingRecovery"/>) is held first: the
/// screen shows when the key moves, with "Cancel the move" (when the app manages passkeys) and Continue, so a person whose
/// device wasn't lost sees it before anything else.  The person is already signed in, so either way ends at
/// <see cref="IPasskeyNavigator.SignedInAsync"/>.
/// </para>
/// </summary>
public sealed class PasskeySignInViewModel : PasskeyFlowViewModel
{
    private string _handle = string.Empty;
    private bool _passkeysAvailable = true;
    private PasskeySignIn? _held;

    /// <summary>Creates the view model.</summary>
    /// <param name="client">The passkey client.</param>
    /// <param name="configuration">The app's configuration.</param>
    /// <param name="navigator">The app's navigation.</param>
    /// <param name="logger">Where a failure in the app's navigation is logged, or null.</param>
    public PasskeySignInViewModel(IPasskeyClient client, PasskeyConfiguration configuration, IPasskeyNavigator navigator, ILogger? logger = null)
        : base(client, configuration, navigator, logger)
    {
        Mode = configuration.Experience.SignInModeFor(configuration.Features);
        SignInCommand = new PasskeyCommand(SignInAsync, () => ShowsPasskeyButton && CanStart);
        ContinueCommand = new PasskeyCommand(ContinueAsync, () => ShowsUsername && CanStart && !string.IsNullOrWhiteSpace(Handle));
        CancelRecoveryCommand = new PasskeyCommand(CancelRecoveryAsync, () => ShowsPendingRecovery && OffersCancelRecovery);
        ContinueSignedInCommand = new PasskeyCommand(ContinueSignedInAsync, () => ShowsPendingRecovery);
    }

    /// <inheritdoc />
    public override PasskeyFlow Flow => PasskeyFlow.SignIn;

    /// <summary>The sign-in mode in effect (see <see cref="PasskeyExperience.SignInModeFor"/>).</summary>
    public PasskeySignInMode Mode { get; }

    /// <summary>The heading, with the app name.</summary>
    public string Title => PasskeyText.WithName(Text.SignInTitle, ApplicationName);

    /// <summary>The line under the heading.</summary>
    public string Welcome => Text.Welcome;

    /// <summary>The heading shown while the app takes over after sign-in, with the app name.</summary>
    public string SignedInTitle => PasskeyText.WithName(Text.SignedInTitle, ApplicationName);

    /// <summary>The line under <see cref="SignedInTitle"/>.</summary>
    public string SigningInText => Text.SigningIn;

    /// <summary>Whether the "opening your account" panel shows: signed in, and nothing held for the person to see first.</summary>
    public bool ShowsSigningIn => HasSucceeded && _held is null;

    /// <summary>Whether the notice about a waiting key-set recovery shows, before the app takes over.</summary>
    public bool ShowsPendingRecovery => HasSucceeded && _held?.PendingRecovery is not null;

    /// <summary>Whether the notice offers "Cancel the move": only when the app manages passkeys.</summary>
    public bool OffersCancelRecovery => Configuration.Features.EnableManagement;

    /// <summary>The notice about a waiting recovery, with when the key moves, or empty.</summary>
    public string PendingRecoveryText => _held?.PendingRecovery is { } pending
        ? PasskeyText.WithName(Text.SignInPendingRecoveryMessage, MomentText(pending.EffectiveUtc)) + (OffersCancelRecovery ? string.Empty : " " + Text.SignInPendingRecoveryElsewhereMessage)
        : string.Empty;

    /// <summary>Cancels the waiting recovery, then goes on into the app.</summary>
    public PasskeyCommand CancelRecoveryCommand { get; }

    /// <summary>Leaves the notice and goes on into the app, with the recovery still waiting.</summary>
    public PasskeyCommand ContinueSignedInCommand { get; }

    /// <summary>Whether the username-less passkey button shows.</summary>
    public bool ShowsPasskeyButton => Mode != PasskeySignInMode.UsernameFirst;

    /// <summary>Whether the username field shows.</summary>
    public bool ShowsUsername => Mode != PasskeySignInMode.UsernameLess;

    /// <summary>Whether the note above the username field shows (choice mode).</summary>
    public bool ShowsUsernameHint => Mode == PasskeySignInMode.Choice;

    /// <summary>Whether the link to registration shows.</summary>
    public bool OffersRegistration => PasskeyRecovery.OffersRegistration(Configuration);

    /// <summary>The username the user typed.</summary>
    public string Handle
    {
        get => _handle;
        set
        {
            if (Set(ref _handle, value ?? string.Empty))
            {
                ContinueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Signs in with whatever passkey the operating system offers for the relying party.</summary>
    public PasskeyCommand SignInCommand { get; }

    /// <summary>Signs in the account named in <see cref="Handle"/>.</summary>
    public PasskeyCommand ContinueCommand { get; }

    private bool CanStart => _passkeysAvailable && State != PasskeyViewState.Working;

    /// <summary>
    /// Checks the device can do passkeys, without prompting.  Call it when the screen appears.  A device that can't
    /// shows the unavailable or device-setup screen in place of the form, with no Back (the buttons couldn't work); the
    /// device-setup screen's "try again" checks again.  Back from any later error (a link the app couldn't follow) returns
    /// to that screen, not the form, until a check succeeds.  A screen shown again after a
    /// sign-in (a cached page the app returns to after sign-out) is back on the form.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when the check is done.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (State == PasskeyViewState.Succeeded)
        {
            Hold(null);
            Reset();
        }
        PasskeyCapabilities capabilities = await Client.GetCapabilitiesAsync(cancellationToken);
        _passkeysAvailable = capabilities.IsSupported;
        if (capabilities.IsSupported)
        {
            // A device that was set up while the screen showed the setup error: back to the form.
            RestoreForm();
        }
        else
        {
            PasskeyErrorCategory category = capabilities.ToErrorCategory();
            Fail(new PasskeyError(category, Configuration.Messages.For(category)), () => LoadAsync(CancellationToken.None), replacesForm: true);
        }
        RaiseCommands();
    }

    /// <inheritdoc />
    protected override void RaiseCommands()
    {
        base.RaiseCommands();
        SignInCommand?.RaiseCanExecuteChanged();
        ContinueCommand?.RaiseCanExecuteChanged();
        CancelRecoveryCommand?.RaiseCanExecuteChanged();
        ContinueSignedInCommand?.RaiseCanExecuteChanged();
    }

    /// <inheritdoc />
    protected override void OnStateChanged()
    {
        base.OnStateChanged();
        RaiseHeld();
    }

    // Back from a failed cancel returns to the notice, not the form: the person is signed in.
    /// <inheritdoc />
    protected override PasskeyViewState DismissedState => _held is null ? PasskeyViewState.Ready : PasskeyViewState.Succeeded;

    private Task SignInAsync()
    {
        return RunAsync(token => Client.AuthenticateAsync(token), SignedInAsync);
    }

    private Task ContinueAsync()
    {
        string handle = Handle.Trim();
        return RunAsync(token => Client.AuthenticateWithUsernameAsync(handle, token), SignedInAsync, typedInput: true);
    }

    // The app has moved on; the screen goes back to the form for when it is shown again.  A waiting recovery holds the
    // hand-over until the person has seen it.
    private async Task SignedInAsync(PasskeySignIn signIn)
    {
        if (signIn.PendingRecovery is not null)
        {
            Hold(signIn);
            return;
        }
        await Navigator.SignedInAsync(signIn, PasskeyFlow.SignIn);
        Reset();
    }

    private Task ContinueSignedInAsync()
    {
        PasskeySignIn? held = _held;
        return held is null ? Task.CompletedTask : NavigateAsync(() => HandOverAsync(held));
    }

    // A recovery that is already gone (it moved, or another device cancelled it) says so; Back returns to the notice.
    private Task CancelRecoveryAsync()
    {
        PasskeySignIn? held = _held;
        if (held?.PendingRecovery is not { } pending)
        {
            return Task.CompletedTask;
        }
        return RunAsync(async token =>
        {
            PasskeyResult cancelled = await Client.CancelKeySetRecoveryAsync(pending.RecoveryId, token);
            if (cancelled.Succeeded)
            {
                return PasskeyResult<bool>.Success(true);
            }
            PasskeyError error = cancelled.Error!;
            return PasskeyResult<bool>.Failure(error.BackendCode == HttpPasskeyBackend.BackendCodes.NotFound
                ? new PasskeyError(error.Category, Text.RecoveryGoneMessage, error.BackendCode)
                : error);
        }, _ => HandOverAsync(held), workingText: Text.Updating);
    }

    private async Task HandOverAsync(PasskeySignIn signIn)
    {
        await Navigator.SignedInAsync(signIn, PasskeyFlow.SignIn);
        Hold(null);
        Reset();
    }

    private void Hold(PasskeySignIn? signIn)
    {
        _held = signIn;
        RaiseHeld();
        RaiseCommands();
    }

    private void RaiseHeld()
    {
        OnPropertyChanged(nameof(ShowsSigningIn));
        OnPropertyChanged(nameof(ShowsPendingRecovery));
        OnPropertyChanged(nameof(PendingRecoveryText));
    }

    private static string MomentText(DateTime utc)
    {
        DateTime asUtc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc;
        return asUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    }
}
