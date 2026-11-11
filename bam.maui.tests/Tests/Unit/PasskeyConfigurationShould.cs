using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyConfiguration Should", Selector = "pkcf")]
public class PasskeyConfigurationShould : UnitTestMenuContainer
{
    public PasskeyConfigurationShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void AcceptCoherentConfiguration()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("validates a valid configuration and reads its defaults", (configuration, reg) =>
        {
            return new DefaultsOutcome(
                !Throws(configuration, c => { }),
                configuration.Features.EnableRegistration,
                configuration.Features.EnableAuthentication,
                configuration.Features.EnableManagement,
                configuration.Features.EnableUsernameLessAuthentication,
                configuration.Branding.ThemeMode,
                configuration.Branding.CornerRadius);
        })
        .TheTest
        .ShouldPass<DefaultsOutcome>((because, outcome) =>
        {
            because.ItsTrue("a valid configuration passes", outcome.Valid);
            because.ItsTrue("registration is on by default", outcome.Registration);
            because.ItsTrue("authentication is on by default", outcome.Authentication);
            because.ItsTrue("management is on by default", outcome.Management);
            because.ItsTrue("username-less sign-in is on by default", outcome.UsernameLess);
            because.ItsTrue("the theme follows the system by default", outcome.Theme == PasskeyThemeMode.System);
            because.ItsTrue("the default corner radius is 12", outcome.Radius == 12);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RejectIncoherentConfiguration()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("validates each broken variant", (unused, reg) =>
        {
            return new RejectionOutcome(
                ThrowsFor(c => c.RelyingPartyId = ""),
                ThrowsFor(c => c.RelyingPartyId = "https://example.com"),
                ThrowsFor(c => c.RelyingPartyId = "example.com:8443"),
                ThrowsFor(c => c.RelyingPartyId = "Example.com"),
                ThrowsFor(c => c.Features = null!),
                ThrowsFor(c => c.Branding = null!),
                ThrowsFor(c => c.Messages = null!),
                ThrowsFor(c => c.Features = new PasskeyFeatures { EnableRegistration = false, EnableAuthentication = false, EnableManagement = false, EnableUsernameLessAuthentication = false }),
                ThrowsFor(c => c.Features = new PasskeyFeatures { EnableAuthentication = false }),
                !ThrowsFor(c => c.Features = new PasskeyFeatures { EnableAuthentication = false, EnableUsernameLessAuthentication = false }),
                ThrowsFor(c => c.Messages.NetworkFailure = " "));
        })
        .TheTest
        .ShouldPass<RejectionOutcome>((because, outcome) =>
        {
            because.ItsTrue("a blank rp id is refused", outcome.BlankRpId);
            because.ItsTrue("an rp id with a scheme is refused", outcome.RpIdWithScheme);
            because.ItsTrue("an rp id with a port is refused", outcome.RpIdWithPort);
            because.ItsTrue("a mixed-case rp id is refused", outcome.RpIdMixedCase);
            because.ItsTrue("null features are refused", outcome.NullFeatures);
            because.ItsTrue("null branding is refused", outcome.NullBranding);
            because.ItsTrue("null messages are refused", outcome.NullMessages);
            because.ItsTrue("everything switched off is refused", outcome.NothingEnabled);
            because.ItsTrue("username-less sign-in without authentication is refused", outcome.UsernameLessWithoutAuthentication);
            because.ItsTrue("registration-only is accepted", outcome.RegistrationOnlyAccepted);
            because.ItsTrue("a blank message is refused", outcome.BlankMessage);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ValidateBranding()
    {
        After.Setup(reg => reg.Set(new PasskeyBranding { ApplicationName = "Example" }))
        .When<PasskeyBranding>("validates the defaults and each broken variant", (branding, reg) =>
        {
            return new BrandingOutcome(
                !BrandingThrows(b => { }),
                !BrandingThrows(b => b.PrimaryColor = "#80FF0000"),
                BrandingThrows(b => b.ApplicationName = " "),
                BrandingThrows(b => b.PrimaryColor = "blue"),
                BrandingThrows(b => b.DarkTextColor = "#FFF"),
                BrandingThrows(b => b.BackgroundColor = null!),
                BrandingThrows(b => b.CornerRadius = -1),
                BrandingThrows(b => b.CornerRadius = double.NaN),
                !BrandingThrows(b => { b.Logo = "logo.png"; b.FontFamily = "Brand"; b.ThemeMode = PasskeyThemeMode.Dark; b.CornerRadius = 0; }));
        })
        .TheTest
        .ShouldPass<BrandingOutcome>((because, outcome) =>
        {
            because.ItsTrue("default branding is valid", outcome.DefaultsValid);
            because.ItsTrue("an eight-digit hex colour is accepted", outcome.ArgbAccepted);
            because.ItsTrue("a blank application name is refused", outcome.BlankName);
            because.ItsTrue("a named colour is refused", outcome.NamedColour);
            because.ItsTrue("a three-digit hex colour is refused", outcome.ShortColour);
            because.ItsTrue("a null colour is refused", outcome.NullColour);
            because.ItsTrue("a negative radius is refused", outcome.NegativeRadius);
            because.ItsTrue("a NaN radius is refused", outcome.NanRadius);
            because.ItsTrue("logo, font, dark theme and square corners are accepted", outcome.OptionalValuesAccepted);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void InterpretCapabilities()
    {
        After.Setup(reg => reg.Set(new PasskeyCapabilities()))
        .When<PasskeyCapabilities>("maps each availability to supported and to an error category", (defaults, reg) =>
        {
            return new CapabilityOutcome(
                defaults.IsSupported,
                defaults.Availability,
                Category(PasskeyAvailability.Available),
                new PasskeyCapabilities { Availability = PasskeyAvailability.Available }.IsSupported,
                Category(PasskeyAvailability.NotSupported),
                Category(PasskeyAvailability.UnsupportedOperatingSystem),
                Category(PasskeyAvailability.MissingCredentialProvider),
                Category(PasskeyAvailability.DeviceSecurityNotConfigured),
                PasskeyCapabilities.Unsupported("Tizen").Platform,
                new UnsupportedPasskeyAuthenticator().GetCapabilitiesAsync().GetAwaiter().GetResult().IsSupported);
        })
        .TheTest
        .ShouldPass<CapabilityOutcome>((because, outcome) =>
        {
            because.ItsTrue("capabilities are not supported until an authenticator says so", !outcome.DefaultSupported);
            because.ItsTrue("the default availability is NotSupported", outcome.DefaultAvailability == PasskeyAvailability.NotSupported);
            because.ItsTrue("available maps to no error", outcome.Available == PasskeyErrorCategory.None);
            because.ItsTrue("available is supported", outcome.AvailableSupported);
            because.ItsTrue("not supported maps to unsupported operating system", outcome.NotSupported == PasskeyErrorCategory.UnsupportedOperatingSystem);
            because.ItsTrue("an old operating system maps to unsupported operating system", outcome.OldOs == PasskeyErrorCategory.UnsupportedOperatingSystem);
            because.ItsTrue("a missing provider keeps its own category", outcome.MissingProvider == PasskeyErrorCategory.MissingCredentialProvider);
            because.ItsTrue("missing device security keeps its own category", outcome.NoDeviceSecurity == PasskeyErrorCategory.DeviceSecurityNotConfigured);
            because.ItsTrue("the unsupported factory carries the platform name", outcome.Platform == "Tizen");
            because.ItsTrue("the unsupported authenticator never reports support", !outcome.UnsupportedAuthenticatorSupported);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseEmptyWebAuthnPayloads()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("constructs each payload wrapper with blank and real JSON", (unused, reg) =>
        {
            return new PayloadOutcome(
                PayloadThrows(() => new WebAuthnCreationOptions(" ")),
                PayloadThrows(() => new WebAuthnRequestOptions("")),
                PayloadThrows(() => new WebAuthnRegistrationResponse(null!)),
                PayloadThrows(() => new WebAuthnAssertionResponse("\t")),
                new WebAuthnCreationOptions("{\"challenge\":\"secret\"}").ToString(),
                new WebAuthnAssertionResponse("{\"signature\":\"secret\"}").ToString());
        })
        .TheTest
        .ShouldPass<PayloadOutcome>((because, outcome) =>
        {
            because.ItsTrue("blank creation options are refused", outcome.BlankCreation);
            because.ItsTrue("empty request options are refused", outcome.EmptyRequest);
            because.ItsTrue("a null registration response is refused", outcome.NullRegistration);
            because.ItsTrue("a whitespace assertion response is refused", outcome.BlankAssertion);
            because.ItsTrue("creation options do not print their JSON", !outcome.CreationText.Contains("secret"));
            because.ItsTrue("an assertion response does not print its JSON", !outcome.AssertionText.Contains("secret"));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static PasskeyErrorCategory Category(PasskeyAvailability availability)
    {
        return new PasskeyCapabilities { Availability = availability }.ToErrorCategory();
    }

    private static bool ThrowsFor(Action<PasskeyConfiguration> mutate)
    {
        return Throws(PasskeyClientFixture.ValidConfiguration(), mutate);
    }

    private static bool Throws(PasskeyConfiguration configuration, Action<PasskeyConfiguration> mutate)
    {
        mutate(configuration);
        try
        {
            configuration.Validate();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool BrandingThrows(Action<PasskeyBranding> mutate)
    {
        PasskeyBranding branding = new PasskeyBranding { ApplicationName = "Example" };
        mutate(branding);
        try
        {
            branding.Validate();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool PayloadThrows(Action construct)
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

    private sealed record DefaultsOutcome(bool Valid, bool Registration, bool Authentication, bool Management, bool UsernameLess, PasskeyThemeMode Theme, double Radius);

    private sealed record RejectionOutcome(bool BlankRpId, bool RpIdWithScheme, bool RpIdWithPort, bool RpIdMixedCase, bool NullFeatures, bool NullBranding, bool NullMessages, bool NothingEnabled, bool UsernameLessWithoutAuthentication, bool RegistrationOnlyAccepted, bool BlankMessage);

    private sealed record BrandingOutcome(bool DefaultsValid, bool ArgbAccepted, bool BlankName, bool NamedColour, bool ShortColour, bool NullColour, bool NegativeRadius, bool NanRadius, bool OptionalValuesAccepted);

    private sealed record CapabilityOutcome(bool DefaultSupported, PasskeyAvailability DefaultAvailability, PasskeyErrorCategory Available, bool AvailableSupported, PasskeyErrorCategory NotSupported, PasskeyErrorCategory OldOs, PasskeyErrorCategory MissingProvider, PasskeyErrorCategory NoDeviceSecurity, string Platform, bool UnsupportedAuthenticatorSupported);

    private sealed record PayloadOutcome(bool BlankCreation, bool EmptyRequest, bool NullRegistration, bool BlankAssertion, string CreationText, string AssertionText);
}
