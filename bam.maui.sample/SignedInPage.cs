using Bam.Maui.Passkeys;

namespace Bam.Maui.Sample;

/// <summary>
/// The sample's signed-in page: who is signed in (only what the backend sent), a way to manage passkeys, and sign-out.
/// It wears the same theme as the passkey screens, so the brand comes from one place.
/// </summary>
public sealed class SignedInPage : ContentPage
{
    private readonly IPasskeyClient _client;
    private readonly SampleNavigator _navigator;
    private readonly Label _status;
    private readonly Button _signOut;

    /// <summary>Creates the page.</summary>
    /// <param name="signIn">The signed-in account, as the backend reported it.</param>
    /// <param name="client">The passkey client, for sign-out.</param>
    /// <param name="navigator">The sample's navigation.</param>
    /// <param name="theme">The passkey screens' theme.</param>
    public SignedInPage(PasskeySignIn signIn, IPasskeyClient client, SampleNavigator navigator, PasskeyTheme theme)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(theme);
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        Resources.MergedDictionaries.Add(theme.Resources);
        this.SetDynamicResource(BackgroundColorProperty, PasskeyResourceKeys.Background);
        Title = "Signed in";

        Label who = new Label { Text = Describe(signIn), FontSize = 20 };
        who.SetDynamicResource(Label.TextColorProperty, PasskeyResourceKeys.Text);
        _status = new Label { IsVisible = false };
        _status.SetDynamicResource(Label.TextColorProperty, PasskeyResourceKeys.Error);
        Button manage = new Button { Text = "Manage passkeys" };
        manage.SetDynamicResource(Button.BackgroundColorProperty, PasskeyResourceKeys.Primary);
        manage.SetDynamicResource(Button.TextColorProperty, PasskeyResourceKeys.OnPrimary);
        manage.Clicked += async (_, _) => await _navigator.ManagePasskeysAsync(this);
        _signOut = new Button { Text = "Sign out" };
        _signOut.SetDynamicResource(Button.BackgroundColorProperty, PasskeyResourceKeys.Secondary);
        _signOut.SetDynamicResource(Button.TextColorProperty, PasskeyResourceKeys.OnPrimary);
        _signOut.Clicked += async (_, _) => await SignOutAsync();

        Content = new VerticalStackLayout { Padding = 24, Spacing = 16, Children = { who, manage, _signOut, _status } };
    }

    /// <summary>The line naming the account: the display name when the backend sent one, and the handle.</summary>
    /// <param name="signIn">The signed-in account.</param>
    /// <returns>The text shown.</returns>
    public static string Describe(PasskeySignIn signIn)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        return string.IsNullOrWhiteSpace(signIn.DisplayName)
            ? "Signed in as " + signIn.Handle
            : "Signed in as " + signIn.DisplayName + " (" + signIn.Handle + ")";
    }

    private async Task SignOutAsync()
    {
        _signOut.IsEnabled = false;
        _status.IsVisible = false;
        PasskeyResult<PasskeySignOut> result = await _client.SignOutAsync();
        if (result.Succeeded && result.Value is not null)
        {
            await _navigator.SignedOutAsync(result.Value);
            return;
        }
        _status.Text = result.Error?.Message ?? "Sign-out didn't finish. Try again.";
        _status.IsVisible = true;
        _signOut.IsEnabled = true;
    }
}
