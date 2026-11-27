using Bam.DependencyInjection;
using Bam.Maui.Passkeys;

namespace Bam.Maui.Sample;

/// <summary>
/// The sample's navigation: it swaps the window's root between the signed-out screens and the signed-in page, and pushes
/// passkey management on top of the signed-in page.  Pages come from the registry on each move, so each screen starts
/// fresh.
/// </summary>
public sealed class SampleNavigator : IPasskeyNavigator
{
    private readonly ServiceRegistry _registry;
    private readonly Func<Window?> _window;

    /// <summary>Creates the navigator.</summary>
    /// <param name="registry">The registry holding the passkey screens.</param>
    /// <param name="window">The app's window, or null before it exists.</param>
    public SampleNavigator(ServiceRegistry registry, Func<Window?> window)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>The page the app opens on.</summary>
    /// <returns>The sign-in screen in a navigation page.</returns>
    public Page StartPage() => new NavigationPage(_registry.Get<PasskeySignInPage>());

    /// <inheritdoc />
    public Task SignedInAsync(PasskeySignIn signIn, PasskeyFlow flow)
    {
        SignedInPage page = new SignedInPage(signIn, _registry.Get<IPasskeyClient>(), this, _registry.Get<PasskeyTheme>());
        return ShowRootAsync(new NavigationPage(page));
    }

    /// <inheritdoc />
    public Task AlternativeSignInRequestedAsync()
    {
        // The sample sets no AlternativeSignInLabel, so the screens never offer this.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RegistrationRequestedAsync() => ShowRootAsync(new NavigationPage(_registry.Get<PasskeyRegistrationPage>()));

    /// <inheritdoc />
    public Task SignInRequestedAsync() => ShowRootAsync(StartPage());

    /// <inheritdoc />
    public Task SignedOutAsync(PasskeySignOut signOut) => SignInRequestedAsync();

    /// <summary>Shows passkey management on top of the current page.</summary>
    /// <param name="from">The page the user is on.</param>
    /// <returns>A task that completes when the page is shown.</returns>
    public Task ManagePasskeysAsync(Page from)
    {
        ArgumentNullException.ThrowIfNull(from);
        return from.Navigation.PushAsync(_registry.Get<PasskeyManagementPage>());
    }

    private Task ShowRootAsync(Page page)
    {
        // A move with no window to make it in fails, so the library's screen reports a navigation failure instead of
        // waiting on a move that never happened.
        Window window = _window() ?? throw new InvalidOperationException("The app has no window to navigate in yet.");
        window.Page = page;
        return Task.CompletedTask;
    }
}
