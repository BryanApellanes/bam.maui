using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Test;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyTheme Should", Selector = "pkth")]
public class PasskeyThemeShould : UnitTestMenuContainer
{
    public PasskeyThemeShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void PutTheBrandInTheResources()
    {
        After.Setup(reg => reg.Set(new PasskeyBranding { ApplicationName = "Example", PrimaryColor = "#FF0B5FFF", SecondaryColor = "#FF222222", CornerRadius = 8.5, FontFamily = "Brand" }))
        .When<PasskeyBranding>("builds a theme for the brand", (branding, reg) =>
        {
            PasskeyTheme theme = new PasskeyTheme(branding);
            string[] keys =
            {
                PasskeyResourceKeys.Primary, PasskeyResourceKeys.OnPrimary, PasskeyResourceKeys.Secondary, PasskeyResourceKeys.Background, PasskeyResourceKeys.Text,
                PasskeyResourceKeys.MutedText, PasskeyResourceKeys.Error, PasskeyResourceKeys.CornerRadius, PasskeyResourceKeys.FontFamily
            };
            string missing = string.Join(",", keys.Where(key => !theme.Resources.ContainsKey(key)));
            PasskeyTheme noFont = new PasskeyTheme(new PasskeyBranding { ApplicationName = "Example" });
            return new BrandOutcome(missing, Hex(theme, PasskeyResourceKeys.Primary), Hex(theme, PasskeyResourceKeys.Secondary), Hex(theme, PasskeyResourceKeys.Background),
                Hex(theme, PasskeyResourceKeys.Text), Hex(branding.BackgroundColor), Hex(branding.TextColor), ((Color)theme.Resources[PasskeyResourceKeys.MutedText]).Alpha,
                (int)theme.Resources[PasskeyResourceKeys.CornerRadius], (string)theme.Resources[PasskeyResourceKeys.FontFamily], noFont.Resources.ContainsKey(PasskeyResourceKeys.FontFamily),
                theme.Scheme, Throws(() => new PasskeyTheme(new PasskeyBranding { ApplicationName = "Example", PrimaryColor = "blue" })), Throws(() => new PasskeyTheme(null!)));
        })
        .TheTest
        .ShouldPass<BrandOutcome>((because, outcome) =>
        {
            because.ItsTrue("every resource key is set: missing " + outcome.Missing, outcome.Missing.Length == 0);
            because.ItsTrue("the primary colour is the brand's: " + outcome.Primary, outcome.Primary == "#FF0B5FFF");
            because.ItsTrue("the secondary colour is the brand's: " + outcome.Secondary, outcome.Secondary == "#FF222222");
            because.ItsTrue("the background starts light: " + outcome.Background, outcome.Background == outcome.LightBackground);
            because.ItsTrue("the text starts light: " + outcome.Text, outcome.Text == outcome.LightText);
            because.ItsTrue("muted text is the text colour, faded", Math.Abs(outcome.MutedAlpha - 0.72f) < 0.01f);
            because.ItsTrue("the corner radius rounds to a whole number: " + outcome.Radius, outcome.Radius == 9);
            because.ItsTrue("the font family is the brand's", outcome.Font == "Brand");
            because.ItsTrue("no font family entry without a brand font", !outcome.FontWithoutBrandFont);
            because.ItsTrue("the scheme starts light", outcome.Scheme == AppTheme.Light);
            because.ItsTrue("an invalid brand is refused", outcome.InvalidThrows);
            because.ItsTrue("a null brand is refused", outcome.NullThrows);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void FollowTheThemeMode()
    {
        After.Setup(reg => reg.Set(new PasskeyBranding { ApplicationName = "Example" }))
        .When<PasskeyBranding>("applies light and dark to a system, a light and a dark brand", (branding, reg) =>
        {
            PasskeyTheme system = new PasskeyTheme(branding);
            system.Apply(AppTheme.Dark);
            SchemeState systemDark = State(system);
            system.Apply(AppTheme.Unspecified);
            SchemeState systemUnspecified = State(system);

            PasskeyTheme light = new PasskeyTheme(new PasskeyBranding { ApplicationName = "Example", ThemeMode = PasskeyThemeMode.Light });
            light.Apply(AppTheme.Dark);
            PasskeyTheme dark = new PasskeyTheme(new PasskeyBranding { ApplicationName = "Example", ThemeMode = PasskeyThemeMode.Dark });
            SchemeState darkAtStart = State(dark);
            dark.Apply(AppTheme.Light);
            return new ModeOutcome(systemDark, systemUnspecified, State(light), darkAtStart, State(dark), Hex(branding.DarkBackgroundColor), Hex(branding.DarkTextColor),
                Hex(branding.DarkErrorColor), Hex(branding.BackgroundColor), Hex(branding.ErrorColor));
        })
        .TheTest
        .ShouldPass<ModeOutcome>((because, outcome) =>
        {
            because.ItsTrue("a system brand goes dark with the device", outcome.SystemDark.Scheme == AppTheme.Dark);
            because.ItsTrue("with the dark background, text and error colours",
                outcome.SystemDark.Background == outcome.DarkBackground && outcome.SystemDark.Text == outcome.DarkText && outcome.SystemDark.Error == outcome.DarkError);
            because.ItsTrue("an unspecified scheme reads as light", outcome.SystemUnspecified.Scheme == AppTheme.Light && outcome.SystemUnspecified.Background == outcome.LightBackground);
            because.ItsTrue("the light error colour comes back with it", outcome.SystemUnspecified.Error == outcome.LightError);
            because.ItsTrue("a light brand stays light on a dark device", outcome.LightOnDark.Scheme == AppTheme.Light && outcome.LightOnDark.Background == outcome.LightBackground);
            because.ItsTrue("a dark brand starts dark", outcome.DarkAtStart.Scheme == AppTheme.Dark && outcome.DarkAtStart.Background == outcome.DarkBackground);
            because.ItsTrue("a dark brand stays dark on a light device", outcome.DarkOnLight.Scheme == AppTheme.Dark && outcome.DarkOnLight.Background == outcome.DarkBackground);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void PickTheMoreReadableTextColour()
    {
        After.Setup(reg => reg.Set(new PasskeyBranding { ApplicationName = "Example" }))
        .When<PasskeyBranding>("picks text for white, black, yellow, navy and mid-grey backgrounds", (branding, reg) =>
        {
            PasskeyTheme yellowBrand = new PasskeyTheme(new PasskeyBranding { ApplicationName = "Example", PrimaryColor = "#FFFFD400" });
            return new ContrastOutcome(
                PasskeyTheme.OnColor(Colors.White) == Colors.Black,
                PasskeyTheme.OnColor(Colors.Black) == Colors.White,
                PasskeyTheme.OnColor(Color.FromArgb("#FFFFD400")) == Colors.Black,
                PasskeyTheme.OnColor(Color.FromArgb("#FF001F5B")) == Colors.White,
                PasskeyTheme.OnColor(Color.FromArgb("#FF777777")) == Colors.Black,
                PasskeyTheme.Luminance(Colors.White),
                PasskeyTheme.Luminance(Colors.Black),
                (Color)yellowBrand.Resources[PasskeyResourceKeys.OnPrimary] == Colors.Black,
                Throws(() => PasskeyTheme.OnColor(null!)));
        })
        .TheTest
        .ShouldPass<ContrastOutcome>((because, outcome) =>
        {
            because.ItsTrue("black on white", outcome.WhiteGetsBlack);
            because.ItsTrue("white on black", outcome.BlackGetsWhite);
            because.ItsTrue("black on yellow", outcome.YellowGetsBlack);
            because.ItsTrue("white on navy", outcome.NavyGetsWhite);
            because.ItsTrue("black on mid-grey #777, where black has the higher ratio", outcome.GreyGetsBlack);
            because.ItsTrue("white has luminance 1", Math.Abs(outcome.WhiteLuminance - 1) < 0.0001);
            because.ItsTrue("black has luminance 0", outcome.BlackLuminance < 0.0001);
            because.ItsTrue("the on-primary resource follows the brand's primary colour", outcome.BrandOnPrimaryBlack);
            because.ItsTrue("a null colour is refused", outcome.NullThrows);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static SchemeState State(PasskeyTheme theme)
    {
        return new SchemeState(theme.Scheme, Hex(theme, PasskeyResourceKeys.Background), Hex(theme, PasskeyResourceKeys.Text), Hex(theme, PasskeyResourceKeys.Error));
    }

    private static string Hex(PasskeyTheme theme, string key)
    {
        return ((Color)theme.Resources[key]).ToArgbHex(true).ToUpperInvariant();
    }

    private static string Hex(string brandColour)
    {
        return Color.FromArgb(brandColour).ToArgbHex(true).ToUpperInvariant();
    }

    private static bool Throws(Action construct)
    {
        try
        {
            construct();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private sealed record BrandOutcome(string Missing, string Primary, string Secondary, string Background, string Text, string LightBackground, string LightText, float MutedAlpha,
        int Radius, string Font, bool FontWithoutBrandFont, AppTheme Scheme, bool InvalidThrows, bool NullThrows);

    private sealed record SchemeState(AppTheme Scheme, string Background, string Text, string Error);

    private sealed record ModeOutcome(SchemeState SystemDark, SchemeState SystemUnspecified, SchemeState LightOnDark, SchemeState DarkAtStart, SchemeState DarkOnLight,
        string DarkBackground, string DarkText, string DarkError, string LightBackground, string LightError);

    private sealed record ContrastOutcome(bool WhiteGetsBlack, bool BlackGetsWhite, bool YellowGetsBlack, bool NavyGetsWhite, bool GreyGetsBlack, double WhiteLuminance,
        double BlackLuminance, bool BrandOnPrimaryBlack, bool NullThrows);
}
