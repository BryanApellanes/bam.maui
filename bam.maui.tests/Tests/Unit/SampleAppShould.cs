using System.Text;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Sample;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Sample App Should", Selector = "pksa")]
public class SampleAppShould : UnitTestMenuContainer
{
    public SampleAppShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void GiveEachLevelItsOwnRelyingParty()
    {
        After.Setup(reg => reg.Set(SampleSettings.ReadBundled()))
        .When<SampleSettings>("reads the prod, test and dev settings and builds each backend", (prod, reg) =>
        {
            SampleSettings test = Bundled("samplesettings.test.json");
            SampleSettings dev = Bundled("samplesettings.dev.json");
            return new LevelOutcome(Describe(prod), Describe(test), Describe(dev), prod.ToConfiguration().Experience.AskForEmail);
        })
        .TheTest
        .ShouldPass<LevelOutcome>((because, outcome) =>
        {
            because.ItsTrue("prod signs in to whoiam.is through www: " + outcome.Prod, outcome.Prod == "whoiam.is|https://www.whoiam.is/account/|Passkey Sample");
            because.ItsTrue("test is its own relying party: " + outcome.Test, outcome.Test == "test.whoiam.is|https://test.whoiam.is/account/|Passkey Sample (test)");
            because.ItsTrue("dev is its own relying party: " + outcome.Dev, outcome.Dev == "dev.whoiam.is|https://dev.whoiam.is/account/|Passkey Sample (dev)");
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
                BackendRefused("{\"relyingPartyId\":\"acme.example\",\"accountEndpoint\":\"http://acme.example/account/\"}"),
                ReadRefused("{\"relyingPartyId\":\"acme.example\",\"relyingPartyId\":\"evil.example\"}"),
                ReadRefused("{\"branding\":{\"themeMode\":2}}"),
                ReadRefused("{\"branding\":{\"themeMode\":\"Sepia\"}}"),
                Read("{\"branding\":{\"themeMode\":\"Dark\"}}").Branding.ThemeMode == PasskeyThemeMode.Dark);
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
            because.ItsTrue("a key given twice is refused", outcome.DuplicateKey);
            because.ItsTrue("a theme mode given as a number is refused", outcome.NumericThemeMode);
            because.ItsTrue("an undefined theme mode is refused", outcome.UndefinedThemeMode);
            because.ItsTrue("a theme mode is read by its name", outcome.NamedThemeMode);
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

    [UnitTest]
    public void MoveBetweenTheScreens()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("signs in, opens management, signs out, then signs out while the server fails", (log, reg) =>
        {
            FakePasskeyBackend backend = new FakePasskeyBackend(log);
            ServiceRegistry registry = new ServiceRegistry();
            registry.For<IPasskeyBackend>().Use(backend);
            registry.For<IPasskeyAuthenticator>().Use(new FakePasskeyAuthenticator(log));
            registry.AddPasskeyClient(PasskeyClientFixture.ValidConfiguration());
            Window? window = null;
            SampleNavigator navigator = new SampleNavigator(registry, () => window);
            registry.AddPasskeyUi(navigator);
            window = new Window(navigator.StartPage());
            string start = Root(window);

            navigator.SignedInAsync(new PasskeySignIn { Handle = "ada" }, PasskeyFlow.SignIn).GetAwaiter().GetResult();
            string signedIn = Root(window);
            SignedInPage page = (SignedInPage)((NavigationPage)window.Page!).RootPage;
            page.ManagePasskeysAsync().GetAwaiter().GetResult();
            string managed = Stack(window);

            page.SignOutAsync().GetAwaiter().GetResult();
            string signedOut = Root(window);

            navigator.SignedInAsync(new PasskeySignIn { Handle = "ada" }, PasskeyFlow.SignIn).GetAwaiter().GetResult();
            SignedInPage again = (SignedInPage)((NavigationPage)window.Page!).RootPage;
            backend.Failures["backend.signOut"] = new HttpRequestException("offline");
            again.SignOutAsync().GetAwaiter().GetResult();
            string failedSignOut = Root(window);

            navigator.RegistrationRequestedAsync().GetAwaiter().GetResult();
            string registration = Root(window);

            bool noWindowRefused;
            try
            {
                new SampleNavigator(registry, () => null).SignInRequestedAsync().GetAwaiter().GetResult();
                noWindowRefused = false;
            }
            catch (InvalidOperationException)
            {
                noWindowRefused = true;
            }
            return new NavigationOutcome(start, signedIn, managed, signedOut, failedSignOut, registration, noWindowRefused, log.Calls.Count(call => call == "backend.signOut"));
        })
        .TheTest
        .ShouldPass<NavigationOutcome>((because, outcome) =>
        {
            because.ItsTrue("the app opens on sign-in: " + outcome.Start, outcome.Start == nameof(PasskeySignInPage));
            because.ItsTrue("signing in swaps the root to the signed-in page: " + outcome.SignedIn, outcome.SignedIn == nameof(SignedInPage));
            because.ItsTrue("manage pushes management on top of the signed-in page: " + outcome.Managed, outcome.Managed == nameof(SignedInPage) + ">" + nameof(PasskeyManagementPage));
            because.ItsTrue("signing out returns to sign-in: " + outcome.SignedOut, outcome.SignedOut == nameof(PasskeySignInPage));
            because.ItsTrue("a failed sign-out still returns to sign-in: " + outcome.FailedSignOut, outcome.FailedSignOut == nameof(PasskeySignInPage));
            because.ItsTrue("create an account shows registration: " + outcome.Registration, outcome.Registration == nameof(PasskeyRegistrationPage));
            because.ItsTrue("a move with no window fails instead of passing silently", outcome.NoWindowRefused);
            because.ItsTrue("both sign-outs reached the backend: " + outcome.SignOutCalls, outcome.SignOutCalls == 2);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static string Root(Window window) => ((NavigationPage)window.Page!).RootPage.GetType().Name;

    private static string Stack(Window window) => string.Join(">", ((NavigationPage)window.Page!).Navigation.NavigationStack.Select(page => page.GetType().Name));

    private static SampleSettings Bundled(string resourceName)
    {
        using Stream stream = typeof(SampleAppShould).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(resourceName + " is not embedded in the tests.");
        return SampleSettings.Read(stream);
    }

    // The relying party, the endpoint the backend really uses (it must accept the settings) and the app name.
    private static string Describe(SampleSettings settings)
    {
        PasskeyConfiguration configuration = settings.ToConfiguration();
        PasskeyHttpBackendOptions options = settings.ToBackendOptions();
        using HttpPasskeyBackend backend = HttpPasskeyBackend.Create(configuration, options);
        Uri endpoint = options.AccountEndpoint ?? new Uri("https://" + configuration.RelyingPartyId + "/account/");
        return configuration.RelyingPartyId + "|" + endpoint.AbsoluteUri + "|" + configuration.Branding.ApplicationName;
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

    private sealed record LevelOutcome(string Prod, string Test, string Dev, bool AskForEmail);

    private sealed record BrandOutcome(string RelyingPartyId, string ApplicationName, string PrimaryColor, string? Endpoint, bool AskForEmail);

    private sealed record RefusalOutcome(bool UnknownMember, bool Truncated, bool Empty, bool SchemeInRelyingParty, bool BlankRelyingParty, bool EndpointNotUri, bool EndpointOffSite, bool EndpointNotHttps, bool DuplicateKey, bool NumericThemeMode, bool UndefinedThemeMode, bool NamedThemeMode);

    private sealed record NavigationOutcome(string Start, string SignedIn, string Managed, string SignedOut, string FailedSignOut, string Registration, bool NoWindowRefused, int SignOutCalls);

    private sealed record DescribeOutcome(string HandleOnly, string WithName, string BlankName);
}
