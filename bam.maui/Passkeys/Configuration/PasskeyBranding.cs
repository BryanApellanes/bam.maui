using System.Text.RegularExpressions;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Which colour scheme the library's views use.
/// </summary>
public enum PasskeyThemeMode
{
    /// <summary>Follow the device setting.</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark
}

/// <summary>
/// The brand a host app gives the screens around the native passkey prompts: name, logo, colours,
/// shape and type.  Changing these changes the library's views without touching library code.
/// The operating system's own prompts (biometric sheet, credential picker, Windows Hello) are
/// not affected by anything here and cannot be branded.
/// <para>
/// Colours are hex strings (<c>#RRGGBB</c> or <c>#AARRGGBB</c>) so this class has no UI
/// dependency and can be validated and tested anywhere.
/// </para>
/// </summary>
public sealed class PasskeyBranding
{
    private static readonly Regex HexColor = new Regex(@"\A#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\z", RegexOptions.CultureInvariant);

    /// <summary>The product name shown on every screen. Required.</summary>
    public string ApplicationName { get; set; } = "Application";

    /// <summary>The logo image, as a MAUI image source name (for example <c>brand_logo.png</c>); null shows no logo.</summary>
    public string? Logo { get; set; }

    /// <summary>The colour of primary actions.</summary>
    public string PrimaryColor { get; set; } = "#2457C5";

    /// <summary>The colour of secondary actions and accents.</summary>
    public string SecondaryColor { get; set; } = "#172B4D";

    /// <summary>The page background in the light scheme.</summary>
    public string BackgroundColor { get; set; } = "#FFFFFF";

    /// <summary>The text colour in the light scheme.</summary>
    public string TextColor { get; set; } = "#000000";

    /// <summary>The colour of error headings and messages.</summary>
    public string ErrorColor { get; set; } = "#B3261E";

    /// <summary>The colour of error headings and messages in the dark scheme.</summary>
    public string DarkErrorColor { get; set; } = "#F2B8B5";

    /// <summary>The page background in the dark scheme.</summary>
    public string DarkBackgroundColor { get; set; } = "#121212";

    /// <summary>The text colour in the dark scheme.</summary>
    public string DarkTextColor { get; set; } = "#FFFFFF";

    /// <summary>The corner radius of buttons and cards, in device-independent units.</summary>
    public double CornerRadius { get; set; } = 12;

    /// <summary>The font family alias registered with the app, or null for the platform default.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Which colour scheme to use. Default follows the device.</summary>
    public PasskeyThemeMode ThemeMode { get; set; } = PasskeyThemeMode.System;

    /// <summary>Checks the brand values are usable.</summary>
    /// <exception cref="ArgumentException">The application name is blank, a colour is not a hex colour, or the radius is negative.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationName))
        {
            throw new ArgumentException("PasskeyBranding.ApplicationName is required.");
        }
        RequireColor(PrimaryColor, nameof(PrimaryColor));
        RequireColor(SecondaryColor, nameof(SecondaryColor));
        RequireColor(BackgroundColor, nameof(BackgroundColor));
        RequireColor(TextColor, nameof(TextColor));
        RequireColor(DarkBackgroundColor, nameof(DarkBackgroundColor));
        RequireColor(DarkTextColor, nameof(DarkTextColor));
        RequireColor(ErrorColor, nameof(ErrorColor));
        RequireColor(DarkErrorColor, nameof(DarkErrorColor));
        if (CornerRadius < 0 || double.IsNaN(CornerRadius) || double.IsInfinity(CornerRadius))
        {
            throw new ArgumentException("PasskeyBranding.CornerRadius must be zero or a positive number.");
        }
    }

    private static void RequireColor(string? value, string name)
    {
        if (value is null || !HexColor.IsMatch(value))
        {
            throw new ArgumentException($"PasskeyBranding.{name} '{value}' is not a hex colour (#RRGGBB or #AARRGGBB).");
        }
    }
}
