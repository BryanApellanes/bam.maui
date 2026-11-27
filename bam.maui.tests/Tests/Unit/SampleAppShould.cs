using System.Text;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Sample;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Sample App Should", Selector = "pksa")]
public class SampleAppShould : UnitTestMenuContainer
{
    public SampleAppShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void SignInToWhoiamIsByDefault()
    {
        After.Setup(reg => reg.Set(SampleSettings.ReadBundled()))
        .When<SampleSettings>("reads the bundled settings and builds the configuration", (settings, reg) =>
        {
            PasskeyConfiguration configuration = settings.ToConfiguration();
            PasskeyHttpBackendOptions options = settings.ToBackendOptions();
            return new DefaultOutcome(configuration.RelyingPartyId, configuration.Branding.ApplicationName, options.AccountEndpoint is null, configuration.Experience.AskForEmail);
        })
        .TheTest
        .ShouldPass<DefaultOutcome>((because, outcome) =>
        {
            because.ItsTrue("the default brand is whoiam.is: " + outcome.RelyingPartyId, outcome.RelyingPartyId == "whoiam.is");
            because.ItsTrue("the app names itself from the settings: " + outcome.ApplicationName, outcome.ApplicationName == "Passkey Sample");
            because.ItsTrue("the endpoint defaults to the relying party's /account/", outcome.DefaultEndpoint);
            because.ItsTrue("sign-up doesn't ask for email unless the settings say so", !outcome.AskForEmail);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void WearABrandOnItsOwnDomain()
    {
        After.Setup(reg => reg.Set(Read("{\"relyingPartyId\":\"acme.example\",\"accountEndpoint\":\"https://id.acme.example/account/\",\"askForEmail\":true,\"branding\":{\"applicationName\":\"Acme\",\"primaryColor\":\"#AA0000\"}}")))
        .When<SampleSettings>("builds the configuration for a brand's own domain", (settings, reg) =>
        {
            PasskeyConfiguration configuration = settings.ToConfiguration();
            PasskeyHttpBackendOptions options = settings.ToBackendOptions();
            return new BrandOutcome(configuration.RelyingPartyId, configuration.Branding.ApplicationName, configuration.Branding.PrimaryColor, options.AccountEndpoint?.AbsoluteUri, configuration.Experience.AskForEmail);
        })
        .TheTest
        .ShouldPass<BrandOutcome>((because, outcome) =>
        {
            because.ItsTrue("the relying party is the brand's domain: " + outcome.RelyingPartyId, outcome.RelyingPartyId == "acme.example");
            because.ItsTrue("the name is the brand's: " + outcome.ApplicationName, outcome.ApplicationName == "Acme");
            because.ItsTrue("the colour is the brand's: " + outcome.PrimaryColor, outcome.PrimaryColor == "#AA0000");
            because.ItsTrue("the endpoint is the brand's: " + outcome.Endpoint, outcome.Endpoint == "https://id.acme.example/account/");
            because.ItsTrue("email is asked for when the settings say so", outcome.AskForEmail);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseSettingsThatDontDescribeARelyingParty()
    {
        After.Setup(reg => reg.Set(new SampleSettings()))
        .When<SampleSettings>("reads each broken settings document", (unused, reg) =>
        {
            return new RefusalOutcome(
                ReadRefused("{\"relyingPartId\":\"acme.example\"}"),
                ReadRefused("{\"relyingPartyId\":"),
                ReadRefused("null"),
                ConfigurationRefused("{\"relyingPartyId\":\"https://acme.example\"}"),
                ConfigurationRefused("{\"relyingPartyId\":\"\"}"),
                OptionsRefused("{\"accountEndpoint\":\"not a uri\"}"),
                BackendRefused("{\"relyingPartyId\":\"acme.example\",\"accountEndpoint\":\"https://evil.example/account/\"}"),
                BackendRefused("{\"relyingPartyId\":\"acme.example\",\"accountEndpoint\":\"http://acme.example/account/\"}"));
        })
        .TheTest
        .ShouldPass<RefusalOutcome>((because, outcome) =>
        {
            because.ItsTrue("a misspelt member is refused, not ignored", outcome.UnknownMember);
            because.ItsTrue("truncated JSON is refused", outcome.Truncated);
            because.ItsTrue("an empty document is refused", outcome.Empty);
            because.ItsTrue("a relying party with a scheme is refused", outcome.SchemeInRelyingParty);
            because.ItsTrue("a blank relying party is refused", outcome.BlankRelyingParty);
            because.ItsTrue("an endpoint that isn't a URI is refused", outcome.EndpointNotUri);
            because.ItsTrue("an endpoint off the relying party is refused", outcome.EndpointOffSite);
            because.ItsTrue("an endpoint over plain http is refused", outcome.EndpointNotHttps);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NameTheAccountFromWhatTheBackendSent()
    {
        After.Setup(reg => reg.Set(new PasskeySignIn { Handle = "ada" }))
        .When<PasskeySignIn>("describes a sign-in with and without a display name", (handleOnly, reg) =>
        {
            return new DescribeOutcome(
                SignedInPage.Describe(handleOnly),
                SignedInPage.Describe(handleOnly with { DisplayName = "Ada Lovelace" }),
                SignedInPage.Describe(handleOnly with { DisplayName = "  " }));
        })
        .TheTest
        .ShouldPass<DescribeOutcome>((because, outcome) =>
        {
            because.ItsTrue("only the handle when no name came: " + outcome.HandleOnly, outcome.HandleOnly == "Signed in as ada");
            because.ItsTrue("the name and the handle when both came: " + outcome.WithName, outcome.WithName == "Signed in as Ada Lovelace (ada)");
            because.ItsTrue("a blank name counts as none: " + outcome.BlankName, outcome.BlankName == "Signed in as ada");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static SampleSettings Read(string json)
    {
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return SampleSettings.Read(stream);
    }

    private static bool ReadRefused(string json)
    {
        try
        {
            Read(json);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool ConfigurationRefused(string json)
    {
        try
        {
            Read(json).ToConfiguration();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool OptionsRefused(string json)
    {
        try
        {
            Read(json).ToBackendOptions();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool BackendRefused(string json)
    {
        SampleSettings settings = Read(json);
        try
        {
            using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(settings.ToConfiguration(), settings.ToBackendOptions());
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private sealed record DefaultOutcome(string RelyingPartyId, string ApplicationName, bool DefaultEndpoint, bool AskForEmail);

    private sealed record BrandOutcome(string RelyingPartyId, string ApplicationName, string PrimaryColor, string? Endpoint, bool AskForEmail);

    private sealed record RefusalOutcome(bool UnknownMember, bool Truncated, bool Empty, bool SchemeInRelyingParty, bool BlankRelyingParty, bool EndpointNotUri, bool EndpointOffSite, bool EndpointNotHttps);

    private sealed record DescribeOutcome(string HandleOnly, string WithName, string BlankName);
}
