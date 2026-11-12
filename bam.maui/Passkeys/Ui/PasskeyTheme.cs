namespace Bam.Maui.Passkeys;

/// <summary>The resource keys the library's views read.  An app can replace any of them in <see cref="PasskeyTheme.Resources"/>.</summary>
public static class PasskeyResourceKeys
{
    /// <summary>Primary actions (<see cref="Color"/>).</summary>
    public const string Primary = "Passkey.Primary";

    /// <summary>Text on a primary action, black or white, whichever reads better (<see cref="Color"/>).</summary>
    public const string OnPrimary = "Passkey.OnPrimary";

    /// <summary>Secondary actions and accents (<see cref="Color"/>).</summary>
    public const string Secondary = "Passkey.Secondary";

    /// <summary>Page background for the current scheme (<see cref="Color"/>).</summary>
    public const string Background = "Passkey.Background";

    /// <summary>Body text for the current scheme (<see cref="Color"/>).</summary>
    public const string Text = "Passkey.Text";

    /// <summary>Secondary text for the current scheme: the body text at reduced opacity (<see cref="Color"/>).</summary>
    public const string MutedText = "Passkey.MutedText";

    /// <summary>Error headings and messages for the current scheme (<see cref="Color"/>).</summary>
    public const string Error = "Passkey.Error";

    /// <summary>Button corner radius (<see cref="int"/>).</summary>
    public const string CornerRadius = "Passkey.CornerRadius";

    /// <summary>The brand's font family alias (<see cref="string"/>); absent when the brand uses the platform font.</summary>
    public const string FontFamily = "Passkey.FontFamily";
}

/// <summary>
/// Turns a <see cref="PasskeyBranding"/> into the resources the library's views read through <c>DynamicResource</c>.
/// Colours follow <see cref="PasskeyBranding.ThemeMode"/>: fixed light or dark, or the device's scheme, re-applied when
/// the device switches.  One theme is shared by all the screens of an app.
/// </summary>
public sealed class PasskeyTheme
{
    private readonly PasskeyBranding _branding;
    private Microsoft.Maui.Controls.Application? _followed;

    /// <summary>Creates the theme in the light scheme (or the brand's fixed scheme).</summary>
    /// <param name="branding">The brand; validated here.</param>
    /// <exception cref="ArgumentException">The brand is invalid.</exception>
    public PasskeyTheme(PasskeyBranding branding)
    {
        _branding = branding ?? throw new ArgumentNullException(nameof(branding));
        branding.Validate();
        Resources = new ResourceDictionary();
        Color primary = Color.FromArgb(branding.PrimaryColor);
        Resources[PasskeyResourceKeys.Primary] = primary;
        Resources[PasskeyResourceKeys.OnPrimary] = OnColor(primary);
        Resources[PasskeyResourceKeys.Secondary] = Color.FromArgb(branding.SecondaryColor);
        Resources[PasskeyResourceKeys.CornerRadius] = (int)Math.Round(branding.CornerRadius, MidpointRounding.AwayFromZero);
        if (!string.IsNullOrWhiteSpace(branding.FontFamily))
        {
            Resources[PasskeyResourceKeys.FontFamily] = branding.FontFamily;
        }
        Apply(AppTheme.Light);
    }

    /// <summary>The resources.  The library's views merge this dictionary; change an entry to restyle every screen.</summary>
    public ResourceDictionary Resources { get; }

    /// <summary>The scheme the colours are set for now.</summary>
    public AppTheme Scheme { get; private set; }

    /// <summary>
    /// Sets the scheme-dependent colours.  With a fixed <see cref="PasskeyBranding.ThemeMode"/>, the fixed scheme wins
    /// over <paramref name="requested"/>.
    /// </summary>
    /// <param name="requested">The device's scheme; <see cref="AppTheme.Unspecified"/> means light.</param>
    public void Apply(AppTheme requested)
    {
        AppTheme scheme = _branding.ThemeMode switch
        {
            PasskeyThemeMode.Light => AppTheme.Light,
            PasskeyThemeMode.Dark => AppTheme.Dark,
            _ => requested == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light
        };
        bool dark = scheme == AppTheme.Dark;
        Color text = Color.FromArgb(dark ? _branding.DarkTextColor : _branding.TextColor);
        Resources[PasskeyResourceKeys.Background] = Color.FromArgb(dark ? _branding.DarkBackgroundColor : _branding.BackgroundColor);
        Resources[PasskeyResourceKeys.Text] = text;
        Resources[PasskeyResourceKeys.MutedText] = text.WithAlpha(0.72f);
        Resources[PasskeyResourceKeys.Error] = Color.FromArgb(dark ? _branding.DarkErrorColor : _branding.ErrorColor);
        Scheme = scheme;
    }

    /// <summary>
    /// Applies the app's current scheme and, when the brand follows the device, re-applies it whenever the device switches.
    /// Calling it again for the same app does nothing.  The library's views call it when they load.
    /// </summary>
    /// <param name="application">The running app, or null (no app yet: nothing to follow).</param>
    public void Follow(Microsoft.Maui.Controls.Application? application)
    {
        if (application is null || ReferenceEquals(application, _followed))
        {
            return;
        }
        if (_followed is not null)
        {
            _followed.RequestedThemeChanged -= OnRequestedThemeChanged;
        }
        _followed = application;
        Apply(application.UserAppTheme != AppTheme.Unspecified ? application.UserAppTheme : application.RequestedTheme);
        if (_branding.ThemeMode == PasskeyThemeMode.System)
        {
            application.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    /// <summary>Black or white, whichever has the higher WCAG contrast ratio against a colour.</summary>
    /// <param name="background">The background.</param>
    /// <returns><see cref="Colors.Black"/> or <see cref="Colors.White"/>.</returns>
    public static Color OnColor(Color background)
    {
        ArgumentNullException.ThrowIfNull(background);
        double luminance = Luminance(background);
        double againstWhite = 1.05 / (luminance + 0.05);
        double againstBlack = (luminance + 0.05) / 0.05;
        return againstBlack >= againstWhite ? Colors.Black : Colors.White;
    }

    /// <summary>The WCAG relative luminance of a colour, 0 to 1.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The luminance.</returns>
    public static double Luminance(Color color)
    {
        ArgumentNullException.ThrowIfNull(color);
        return 0.2126 * Linear(color.Red) + 0.7152 * Linear(color.Green) + 0.0722 * Linear(color.Blue);
    }

    private static double Linear(float channel)
    {
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        Apply(e.RequestedTheme);
    }
}
