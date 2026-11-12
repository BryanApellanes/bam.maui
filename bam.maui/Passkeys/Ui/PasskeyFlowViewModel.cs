using System.ComponentModel;
using System.Runtime.CompilerServices;
using Bam.Logging;

namespace Bam.Maui.Passkeys;

/// <summary>Where a screen is in its flow.</summary>
public enum PasskeyViewState
{
    /// <summary>Waiting for the user.</summary>
    Ready = 0,

    /// <summary>A ceremony is running; the system prompt may be up.</summary>
    Working,

    /// <summary>The backend verified the ceremony.</summary>
    Succeeded,

    /// <summary>The ceremony failed; <see cref="PasskeyFlowViewModel.Recovery"/> says what to offer.</summary>
    Failed
}

/// <summary>
/// What the library's screens have in common: the flow state, the error and its recovery plan, cancelling a ceremony
/// in progress, and running the last attempt again.  Commands are meant to be invoked on the UI thread; the view models
/// don't leave it (no <c>ConfigureAwait(false)</c>), so property changes are raised there too.
/// </summary>
public abstract class PasskeyFlowViewModel : INotifyPropertyChanged
{
    private PasskeyViewState _state;
    private PasskeyRecoveryPlan? _recovery;
    private string? _note;
    private CancellationTokenSource? _ceremony;
    private Func<Task>? _lastAttempt;
    private PasskeyError? _formReplacedBy;
    private Func<Task>? _formReplacedRetry;
    private PasskeyFlow _formReplacedFlow;
    private string _workingText = string.Empty;
    private bool _typedInput;
    private readonly ILogger? _logger;

    /// <summary>Creates the view model.</summary>
    /// <param name="client">The passkey client.</param>
    /// <param name="configuration">The app's configuration.</param>
    /// <param name="navigator">The app's navigation.</param>
    /// <param name="logger">Where a failure in the app's navigation is logged (by exception type only), or null.</param>
    protected PasskeyFlowViewModel(IPasskeyClient client, PasskeyConfiguration configuration, IPasskeyNavigator navigator, ILogger? logger = null)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _logger = logger;
        _workingText = configuration.Text.Working;
        CancelCommand = new PasskeyCommand(CancelAsync, () => _ceremony is not null);
        TryAgainCommand = new PasskeyCommand(TryAgainAsync, () => _lastAttempt is not null && State != PasskeyViewState.Working);
        BackCommand = new PasskeyCommand(BackAsync, () => State == PasskeyViewState.Failed && OffersBack);
        UseAnotherWayCommand = new PasskeyCommand(() => NavigateAsync(() => Navigator.AlternativeSignInRequestedAsync()), () => HasAlternative && State != PasskeyViewState.Working);
        GoToRegistrationCommand = new PasskeyCommand(() => NavigateAsync(() => Navigator.RegistrationRequestedAsync()), () => PasskeyRecovery.OffersRegistration(Configuration) && State != PasskeyViewState.Working);
        GoToSignInCommand = new PasskeyCommand(() => NavigateAsync(() => Navigator.SignInRequestedAsync()), () => Configuration.Features.EnableAuthentication && State != PasskeyViewState.Working);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Which flow this screen belongs to.</summary>
    public abstract PasskeyFlow Flow { get; }

    /// <summary>The passkey client the ceremonies run through.</summary>
    protected IPasskeyClient Client { get; }

    /// <summary>The app's navigation.</summary>
    protected IPasskeyNavigator Navigator { get; }

    /// <summary>The app's configuration.</summary>
    public PasskeyConfiguration Configuration { get; }

    /// <summary>The brand.</summary>
    public PasskeyBranding Branding => Configuration.Branding;

    /// <summary>The on-screen text.</summary>
    public PasskeyText Text => Configuration.Text;

    /// <summary>The app name, as the brand gives it.</summary>
    public string ApplicationName => Configuration.Branding.ApplicationName;

    /// <summary>Where the screen is in its flow.</summary>
    public PasskeyViewState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(IsReady));
                OnPropertyChanged(nameof(IsWorking));
                OnPropertyChanged(nameof(HasSucceeded));
                OnPropertyChanged(nameof(HasFailed));
                OnStateChanged();
            }
        }
    }

    /// <summary>Whether the screen waits for the user.</summary>
    public bool IsReady => State == PasskeyViewState.Ready;

    /// <summary>Whether a ceremony is running.</summary>
    public bool IsWorking => State == PasskeyViewState.Working;

    /// <summary>Whether the backend verified the ceremony.</summary>
    public bool HasSucceeded => State == PasskeyViewState.Succeeded;

    /// <summary>Whether the ceremony failed.</summary>
    public bool HasFailed => State == PasskeyViewState.Failed;

    /// <summary>
    /// The error the screen last failed with, set before <see cref="State"/> becomes <see cref="PasskeyViewState.Failed"/>,
    /// so <see cref="OnStateChanged"/> can act on its category whatever the recovery plan offers.  Null until a failure.
    /// </summary>
    protected PasskeyError? LastError { get; private set; }

    /// <summary>Where the screen logs, or null.  Type and category names only, never a value.</summary>
    protected ILogger? Logger => _logger;

    /// <summary>What to offer after a failure, or null.</summary>
    public PasskeyRecoveryPlan? Recovery
    {
        get => _recovery;
        private set
        {
            if (Set(ref _recovery, value))
            {
                OnPropertyChanged(nameof(ErrorTitle));
                OnPropertyChanged(nameof(ErrorMessage));
                OnPropertyChanged(nameof(ErrorHint));
                OnPropertyChanged(nameof(HasErrorHint));
                OnPropertyChanged(nameof(OffersTryAgain));
                OnPropertyChanged(nameof(OffersAnotherWay));
                OnPropertyChanged(nameof(OffersSignInInstead));
                OnPropertyChanged(nameof(OffersCreateAccountInstead));
                OnPropertyChanged(nameof(OffersBack));
                OnPropertyChanged(nameof(OffersSignInAgain));
            }
        }
    }

    /// <summary>The error screen's heading.</summary>
    public string ErrorTitle => Recovery?.Title ?? string.Empty;

    /// <summary>The error screen's message.</summary>
    public string ErrorMessage => Recovery?.Message ?? string.Empty;

    /// <summary>The error screen's hint, or null.</summary>
    public string? ErrorHint => Recovery?.Hint;

    /// <summary>Whether the error screen has a hint.</summary>
    public bool HasErrorHint => !string.IsNullOrEmpty(Recovery?.Hint);

    /// <summary>Whether the error screen offers to try again.</summary>
    public bool OffersTryAgain => Offers(PasskeyRecoveryAction.TryAgain);

    /// <summary>Whether the error screen offers the app's other way to sign in.</summary>
    public bool OffersAnotherWay => Offers(PasskeyRecoveryAction.UseAnotherWay);

    /// <summary>Whether the error screen offers sign-in instead.</summary>
    public bool OffersSignInInstead => Offers(PasskeyRecoveryAction.SignInInstead);

    /// <summary>Whether the error screen offers to create an account instead.</summary>
    public bool OffersCreateAccountInstead => Offers(PasskeyRecoveryAction.CreateAccountInstead);

    /// <summary>
    /// Whether the error screen offers the way back to the form.  Every failure screen does, except the device check's,
    /// which takes the place of a form that can't be used.
    /// </summary>
    public bool OffersBack => Offers(PasskeyRecoveryAction.Back);

    /// <summary>Whether the error screen offers to sign in again (the session has ended).</summary>
    public bool OffersSignInAgain => Offers(PasskeyRecoveryAction.SignInAgain);

    /// <summary>A quiet line on the ready screen (after a dismissed prompt), or null.</summary>
    public string? Note
    {
        get => _note;
        private set
        {
            if (Set(ref _note, value))
            {
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    /// <summary>Whether <see cref="Note"/> has text.</summary>
    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>Whether the app has another way to sign in.</summary>
    public bool HasAlternative => Configuration.Experience.AlternativeSignInLabel is not null;

    /// <summary>The label of the app's other way to sign in, or empty.</summary>
    public string AlternativeLabel => Configuration.Experience.AlternativeSignInLabel ?? string.Empty;

    /// <summary>
    /// The line shown while a call runs: <see cref="PasskeyText.Working"/> while the system prompt is up, or the line
    /// the call asked for (see <see cref="RunAsync{TValue}"/>) when no prompt is.
    /// </summary>
    public string WorkingText
    {
        get => _workingText;
        private set => Set(ref _workingText, value);
    }

    /// <summary>Stops the ceremony in progress.  The ceremony then ends as a cancellation, not a failure.</summary>
    public PasskeyCommand CancelCommand { get; }

    /// <summary>Runs the last attempt again.</summary>
    public PasskeyCommand TryAgainCommand { get; }

    /// <summary>Leaves the error screen for the form, with what the user typed kept (see <see cref="Dismiss"/>).</summary>
    public PasskeyCommand BackCommand { get; }

    /// <summary>Hands over to the app's other way to sign in.</summary>
    public PasskeyCommand UseAnotherWayCommand { get; }

    /// <summary>Hands over to the registration screen.</summary>
    public PasskeyCommand GoToRegistrationCommand { get; }

    /// <summary>Hands over to the sign-in screen.</summary>
    public PasskeyCommand GoToSignInCommand { get; }

    /// <summary>
    /// Clears an error and returns to where the user was before it: the form, with what they typed kept, or (see
    /// <see cref="DismissedState"/>) a success screen whose navigation failed.  The error's "try again" goes with it.
    /// While a failure has replaced the form (see <see cref="Fail(PasskeyError, Func{Task}?, bool, PasskeyFlow?)"/>), that failure's
    /// screen is where the user was: it comes back, with its reason and its "try again", until
    /// <see cref="RestoreForm"/>.
    /// </summary>
    public void Dismiss()
    {
        if (State != PasskeyViewState.Failed)
        {
            return;
        }
        if (_formReplacedBy is not null)
        {
            _typedInput = false;
            _lastAttempt = _formReplacedRetry;
            Fail(_formReplacedBy, true, _formReplacedFlow);
        }
        else
        {
            Recovery = null;
            _lastAttempt = null;
            State = DismissedState;
            RaiseCommands();
        }
    }

    /// <summary>
    /// Ends a failure that replaced the form (the device can do passkeys after all, the list loaded) and returns to the
    /// form if that failure is showing.
    /// </summary>
    protected void RestoreForm()
    {
        _formReplacedBy = null;
        _formReplacedRetry = null;
        Dismiss();
    }

    /// <summary>Where <see cref="Dismiss"/> returns to.  The form, unless a derived screen has somewhere better.</summary>
    protected virtual PasskeyViewState DismissedState => PasskeyViewState.Ready;

    /// <summary>
    /// Puts a finished screen back to the form, for when it is shown again (a cached Shell page, a navigation root the
    /// app returns to after sign-out).  Does nothing while a ceremony runs.
    /// </summary>
    protected void Reset()
    {
        if (State == PasskeyViewState.Working)
        {
            return;
        }
        Recovery = null;
        Note = null;
        _lastAttempt = null;
        State = PasskeyViewState.Ready;
        RaiseCommands();
    }

    /// <summary>
    /// Runs one ceremony: shows progress, cancels on <see cref="CancelCommand"/>, and on failure shows the recovery plan
    /// (or, for a cancellation, returns quietly to the form).
    /// </summary>
    /// <typeparam name="TValue">What the ceremony returns.</typeparam>
    /// <param name="ceremony">The client call.</param>
    /// <param name="succeeded">
    /// What to do with a verified result.  It runs the app's navigation, so a failure in it is caught, logged and shown
    /// as a failure whose "try again" runs it again; it never escapes to the command.
    /// </param>
    /// <param name="typedInput">Whether the ceremony uses what the user typed (see <see cref="PasskeyRecovery.For"/>).</param>
    /// <param name="flow">The flow a failure is reported for, or null for <see cref="Flow"/> (the profile write on the registration screen is <see cref="PasskeyFlow.Profile"/>).</param>
    /// <param name="workingText">The progress line, or null for <see cref="PasskeyText.Working"/>.  A call that puts up no system prompt passes its own line.</param>
    /// <param name="replacesForm">
    /// Whether a failure takes the place of the form, as a list that couldn't load does (see
    /// <see cref="Fail(PasskeyError, Func{Task}?, bool, PasskeyFlow?)"/>); its "try again" is this call.
    /// </param>
    /// <returns>A task that completes when the ceremony and its follow-up are done.</returns>
    protected async Task RunAsync<TValue>(Func<CancellationToken, Task<PasskeyResult<TValue>>> ceremony, Func<TValue, Task> succeeded, bool typedInput = false, PasskeyFlow? flow = null, string? workingText = null, bool replacesForm = false)
    {
        ArgumentNullException.ThrowIfNull(ceremony);
        ArgumentNullException.ThrowIfNull(succeeded);
        if (State == PasskeyViewState.Working)
        {
            return;
        }
        Func<Task> attempt = () => RunAsync(ceremony, succeeded, typedInput, flow, workingText, replacesForm);
        _lastAttempt = attempt;
        _typedInput = typedInput;
        using CancellationTokenSource source = new CancellationTokenSource();
        _ceremony = source;
        Recovery = null;
        Note = null;
        WorkingText = workingText ?? Text.Working;
        State = PasskeyViewState.Working;
        RaiseCommands();
        PasskeyResult<TValue> result;
        try
        {
            result = await ceremony(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // The client reports a cancelled ceremony as a result; a call that throws instead is treated the same.
            result = PasskeyResult<TValue>.Failure(new PasskeyError(PasskeyErrorCategory.UserCancelled, Configuration.Messages.UserCancelled));
        }
        finally
        {
            _ceremony = null;
        }

        if (result.Succeeded)
        {
            State = PasskeyViewState.Succeeded;
            RaiseCommands();
            TValue value = result.Value!;
            await NavigateAsync(() => succeeded(value));
            return;
        }
        if (replacesForm && !result.Error!.IsCancellation)
        {
            Fail(result.Error, attempt, true, flow);
            return;
        }
        Fail(result.Error!, false, flow ?? Flow);
    }

    /// <summary>
    /// Runs one of the app's navigation calls.  A failure in it is caught, logged by exception type, and shown as an
    /// <see cref="PasskeyErrorCategory.Unknown"/> failure whose "try again" makes the same call again.  The app's code
    /// never throws through a command, where an <c>async void</c> handler would crash the app.  A call that succeeds
    /// from an error screen that offers Back (a retried link, the app's other way in) clears the error, so the screen
    /// isn't left on it when the app returns.  It doesn't clear a newer error shown while the call ran.
    /// </summary>
    /// <param name="navigate">The navigation call.</param>
    /// <returns>A task that completes when the call has returned or failed.</returns>
    protected async Task NavigateAsync(Func<Task> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        PasskeyRecoveryPlan? showing = Recovery;
        try
        {
            await navigate();
            if (State == PasskeyViewState.Failed && OffersBack && ReferenceEquals(Recovery, showing))
            {
                Dismiss();
            }
        }
        catch (Exception failed)
        {
            _logger?.Warning("Passkey {0} screen: the app's navigation failed: exception {1}.", Flow.ToString(), failed.GetType().Name);
            _typedInput = false;
            Fail(new PasskeyError(PasskeyErrorCategory.Unknown, Configuration.Messages.For(PasskeyErrorCategory.Unknown)), () => NavigateAsync(navigate), false, Flow);
        }
    }

    /// <summary>Shows a failure that didn't come from a ceremony, such as a device that can't do passkeys.</summary>
    /// <param name="error">The error.</param>
    /// <param name="retry">What <see cref="TryAgainCommand"/> runs, or null to keep the last attempt.</param>
    /// <param name="replacesForm">
    /// Whether the failure screen takes the place of a form that can't be used, so it offers no Back (see
    /// <see cref="PasskeyRecovery.For"/>), and <see cref="Dismiss"/> returns to it, not the form, until
    /// <see cref="RestoreForm"/>.
    /// </param>
    /// <param name="flow">The flow the failure is reported for, or null for <see cref="Flow"/>.</param>
    protected void Fail(PasskeyError error, Func<Task>? retry, bool replacesForm = false, PasskeyFlow? flow = null)
    {
        _typedInput = false;
        PasskeyFlow failed = flow ?? Flow;
        if (replacesForm)
        {
            _formReplacedBy = error;
            _formReplacedRetry = retry;
            _formReplacedFlow = failed;
        }
        if (retry is not null)
        {
            _lastAttempt = retry;
        }
        Fail(error, replacesForm, failed);
    }

    private void Fail(PasskeyError error, bool replacesForm, PasskeyFlow flow)
    {
        PasskeyRecoveryPlan plan = PasskeyRecovery.For(error, flow, Configuration, _typedInput, replacesForm);
        LastError = error;
        if (plan.IsQuiet)
        {
            Recovery = null;
            Note = plan.Message;
            State = PasskeyViewState.Ready;
        }
        else
        {
            Recovery = plan;
            State = PasskeyViewState.Failed;
        }
        RaiseCommands();
    }

    /// <summary>Called after <see cref="State"/> changes, so a derived view model can refresh what depends on it.</summary>
    protected virtual void OnStateChanged()
    {
    }

    /// <summary>Tells bound controls to re-read every command's <c>CanExecute</c>.</summary>
    protected virtual void RaiseCommands()
    {
        CancelCommand.RaiseCanExecuteChanged();
        TryAgainCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        UseAnotherWayCommand.RaiseCanExecuteChanged();
        GoToRegistrationCommand.RaiseCanExecuteChanged();
        GoToSignInCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    /// <param name="propertyName">The property.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Sets a field and raises <see cref="PropertyChanged"/> when the value changed.</summary>
    /// <typeparam name="TField">The field's type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property.</param>
    /// <returns>True when the value changed.</returns>
    protected bool Set<TField>(ref TField field, TField value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<TField>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private bool Offers(PasskeyRecoveryAction action)
    {
        return Recovery is not null && Recovery.Actions.Contains(action);
    }

    private Task CancelAsync()
    {
        _ceremony?.Cancel();
        return Task.CompletedTask;
    }

    private Task BackAsync()
    {
        Dismiss();
        return Task.CompletedTask;
    }

    private Task TryAgainAsync()
    {
        Func<Task>? attempt = _lastAttempt;
        return attempt is null ? Task.CompletedTask : attempt();
    }
}
