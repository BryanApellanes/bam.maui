namespace Bam.Maui.Passkeys;

/// <summary>
/// A ready-made page around <see cref="PasskeySignInView"/>, for an app that navigates between pages.  It runs the
/// device check each time it appears.  Apps that compose their own pages use the view directly.
/// </summary>
public sealed class PasskeySignInPage : ContentPage
{
    /// <summary>Creates the page.</summary>
    /// <param name="view">The sign-in view.</param>
    public PasskeySignInPage(PasskeySignInView view)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Title = view.ViewModel.ApplicationName;
        Content = view;
        this.SetDynamicResource(BackgroundColorProperty, PasskeyResourceKeys.Background);
        Resources.MergedDictionaries.Add(view.Theme.Resources);
    }

    /// <summary>The sign-in view.</summary>
    public PasskeySignInView View { get; }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await View.ViewModel.LoadAsync();
        }
        catch (OperationCanceledException)
        {
            // Nothing cancels this probe today; a cancelled probe says nothing about the device, so the form stays as it is.
        }
    }
}

/// <summary>A ready-made page around <see cref="PasskeyRegistrationView"/>, for an app that navigates between pages.</summary>
public sealed class PasskeyRegistrationPage : ContentPage
{
    /// <summary>Creates the page.</summary>
    /// <param name="view">The registration view.</param>
    public PasskeyRegistrationPage(PasskeyRegistrationView view)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Title = view.ViewModel.ApplicationName;
        Content = view;
        this.SetDynamicResource(BackgroundColorProperty, PasskeyResourceKeys.Background);
        Resources.MergedDictionaries.Add(view.Theme.Resources);
    }

    /// <summary>The registration view.</summary>
    public PasskeyRegistrationView View { get; }
}
