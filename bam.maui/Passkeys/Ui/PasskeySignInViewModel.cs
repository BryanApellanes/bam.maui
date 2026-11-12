using Bam.Logging;

namespace Bam.Maui.Passkeys;

/// <summary>
/// The sign-in screen: the passkey button, the username field the <see cref="PasskeySignInMode"/> asks for,
/// the app's other way to sign in, and the link to registration.  A verified sign-in goes straight to
/// <see cref="IPasskeyNavigator.SignedInAsync"/>.
/// </summary>
public sealed class PasskeySignInViewModel : PasskeyFlowViewModel
{
    private string _handle = string.Empty;
    private bool _passkeysAvailable = true;

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
    }

    /// <inheritdoc />
    public override PasskeyFlow Flow => PasskeyFlow.SignIn;

    /// <summary>The sign-in mode in effect (see <see cref="PasskeyExperience.SignInModeFor"/>).</summary>
    public PasskeySignInMode Mode { get; }

    /// <summary>The heading, with the app name.</summary>
    public string Title => PasskeyText.WithName(Text.SignInTitle, ApplicationName);

    /// <summary>The line under the heading.</summary>
    public string Welcome => Text.Welcome;

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
    /// device-setup screen's "try again" checks again.  A screen shown again after a
    /// sign-in (a cached page the app returns to after sign-out) is back on the form.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>A task that completes when the check is done.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (State == PasskeyViewState.Succeeded)
        {
            Reset();
        }
        PasskeyCapabilities capabilities = await Client.GetCapabilitiesAsync(cancellationToken);
        _passkeysAvailable = capabilities.IsSupported;
        if (capabilities.IsSupported)
        {
            // A device that was set up while the screen showed the setup error: back to the form.
            Dismiss();
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
    }

    private Task SignInAsync()
    {
        return RunAsync(token => Client.AuthenticateAsync(token), SignedInAsync);
    }

    private Task ContinueAsync()
    {
        string handle = Handle.Trim();
        return RunAsync(token => Client.AuthenticateWithUsernameAsync(handle, token), SignedInAsync, typedInput: true);
    }

    // The app has moved on; the screen goes back to the form for when it is shown again.
    private async Task SignedInAsync(PasskeySignIn signIn)
    {
        await Navigator.SignedInAsync(signIn, PasskeyFlow.SignIn);
        Reset();
    }
}
