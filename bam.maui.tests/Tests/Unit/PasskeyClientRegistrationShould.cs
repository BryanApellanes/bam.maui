using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;
using Microsoft.Extensions.DependencyInjection;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyClientRegistration Should", Selector = "pkrg")]
public class PasskeyClientRegistrationShould : UnitTestMenuContainer
{
    public PasskeyClientRegistrationShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void BindTheClientOnTheRegistry()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("adds the passkey client to a host registry that has a backend", (log, reg) =>
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.For<IPasskeyBackend>().Use(new FakePasskeyBackend(log));
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            registry.AddPasskeyClient(configuration);
            IPasskeyClient client = registry.Get<IPasskeyClient>();
            PasskeyResult<PasskeySignIn> signIn = client.AuthenticateAsync().GetAwaiter().GetResult();
            return new BindingOutcome(
                client is PasskeyClient,
                ReferenceEquals(registry.Get<PasskeyConfiguration>(), configuration),
                registry.Get<IPasskeyAuthenticator>() is UnsupportedPasskeyAuthenticator,
                registry.Get<PasskeyErrorNormalizer>() is not null,
                signIn.Error?.Category,
                log.Calls.Count);
        })
        .TheTest
        .ShouldPass<BindingOutcome>((because, outcome) =>
        {
            because.ItsTrue("IPasskeyClient resolves to PasskeyClient", outcome.ClientResolved);
            because.ItsTrue("the configuration is the instance the host passed", outcome.SameConfiguration);
            because.ItsTrue("the authenticator defaults to the unsupported one", outcome.DefaultAuthenticator);
            because.ItsTrue("the normalizer resolves", outcome.NormalizerResolved);
            because.ItsTrue("without a platform authenticator sign-in fails as unsupported", outcome.SignInCategory == PasskeyErrorCategory.UnsupportedOperatingSystem);
            because.ItsTrue("and the backend is never called", outcome.BackendCalls == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RespectHostBindings()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("registers a platform authenticator before and after AddPasskeyClient", (log, reg) =>
        {
            ServiceRegistry before = new ServiceRegistry();
            before.For<IPasskeyBackend>().Use(new FakePasskeyBackend(log));
            before.For<IPasskeyAuthenticator>().Use(new FakePasskeyAuthenticator(log));
            before.AddPasskeyClient(PasskeyClientFixture.ValidConfiguration());
            bool signedInBefore = before.Get<IPasskeyClient>().AuthenticateAsync().GetAwaiter().GetResult().Succeeded;

            ServiceRegistry after = new ServiceRegistry();
            after.For<IPasskeyBackend>().Use(new FakePasskeyBackend(log));
            after.AddPasskeyClient(PasskeyClientFixture.ValidConfiguration());
            after.For<IPasskeyAuthenticator>().Use(new FakePasskeyAuthenticator(log));
            bool signedInAfter = after.Get<IPasskeyClient>().AuthenticateAsync().GetAwaiter().GetResult().Succeeded;

            return new HostBindingOutcome(before.Get<IPasskeyAuthenticator>() is FakePasskeyAuthenticator, signedInBefore, signedInAfter);
        })
        .TheTest
        .ShouldPass<HostBindingOutcome>((because, outcome) =>
        {
            because.ItsTrue("an authenticator bound first is kept", outcome.KeptHostAuthenticator);
            because.ItsTrue("the service uses an authenticator bound before registration", outcome.SignedInBefore);
            because.ItsTrue("the service uses an authenticator bound after registration", outcome.SignedInAfter);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseInvalidSetups()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("registers an invalid configuration, and resolves without a backend", (log, reg) =>
        {
            bool invalidRefused = false;
            try
            {
                new ServiceRegistry().AddPasskeyClient(new PasskeyConfiguration());
            }
            catch (ArgumentException)
            {
                invalidRefused = true;
            }

            ServiceRegistry noBackend = new ServiceRegistry();
            noBackend.AddPasskeyClient(PasskeyClientFixture.ValidConfiguration());
            string? missingBackendMessage = null;
            try
            {
                noBackend.Get<IPasskeyClient>();
            }
            catch (Exception exception)
            {
                missingBackendMessage = (exception.InnerException ?? exception).Message + exception.Message;
            }
            return new InvalidSetupOutcome(invalidRefused, missingBackendMessage);
        })
        .TheTest
        .ShouldPass<InvalidSetupOutcome>((because, outcome) =>
        {
            because.ItsTrue("an invalid configuration is refused at registration", outcome.InvalidRefused);
            because.ItsTrue("resolving without a backend names the missing binding", outcome.MissingBackendMessage is not null && outcome.MissingBackendMessage.Contains("IPasskeyBackend"));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void BridgeIntoTheMauiServiceCollection()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("adds bam passkeys to a service collection and resolves from the provider", (log, reg) =>
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.For<IPasskeyBackend>().Use(new FakePasskeyBackend(log));
            registry.For<IPasskeyAuthenticator>().Use(new FakePasskeyAuthenticator(log));
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            configuration.Branding.ApplicationName = "Bridge";

            ServiceCollection services = new ServiceCollection();
            services.AddBamPasskeys(registry, configuration);
            using ServiceProvider provider = services.BuildServiceProvider();
            IPasskeyClient client = provider.GetRequiredService<IPasskeyClient>();
            return new BridgeOutcome(
                client.AuthenticateAsync().GetAwaiter().GetResult().Succeeded,
                ReferenceEquals(provider.GetRequiredService<PasskeyConfiguration>(), configuration),
                provider.GetRequiredService<PasskeyBranding>().ApplicationName);
        })
        .TheTest
        .ShouldPass<BridgeOutcome>((because, outcome) =>
        {
            because.ItsTrue("IPasskeyClient resolved from MS DI runs a ceremony through the registry's bindings", outcome.SignedIn);
            because.ItsTrue("the configuration is injectable", outcome.SameConfiguration);
            because.ItsTrue("the branding is injectable", outcome.ApplicationName == "Bridge");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void BindTheScreensOnTheRegistry()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("adds the screens before and after the client, and with a null navigator", (log, reg) =>
        {
            ServiceRegistry bare = new ServiceRegistry();
            string? withoutClient = null;
            try
            {
                bare.AddPasskeyUi(new FakePasskeyNavigator());
            }
            catch (InvalidOperationException refused)
            {
                withoutClient = refused.Message;
            }

            ServiceRegistry registry = new ServiceRegistry();
            registry.For<IPasskeyBackend>().Use(new FakePasskeyBackend(log));
            registry.For<IPasskeyAuthenticator>().Use(new FakePasskeyAuthenticator(log));
            PasskeyConfiguration configuration = PasskeyClientFixture.ValidConfiguration();
            registry.AddPasskeyClient(configuration);
            bool nullNavigatorRefused = false;
            try
            {
                registry.AddPasskeyUi(null!);
            }
            catch (ArgumentNullException)
            {
                nullNavigatorRefused = true;
            }
            FakePasskeyNavigator navigator = new FakePasskeyNavigator();
            registry.AddPasskeyUi(navigator);
            PasskeySignInViewModel signIn = registry.Get<PasskeySignInViewModel>();
            PasskeySignInViewModel secondSignIn = registry.Get<PasskeySignInViewModel>();
            PasskeyRegistrationViewModel registration = registry.Get<PasskeyRegistrationViewModel>();
            signIn.SignInCommand.ExecuteAsync().GetAwaiter().GetResult();
            return new ScreensOutcome(withoutClient, nullNavigatorRefused, ReferenceEquals(registry.Get<PasskeyTheme>(), registry.Get<PasskeyTheme>()),
                !ReferenceEquals(signIn, secondSignIn), ReferenceEquals(signIn.Configuration, configuration), ReferenceEquals(registration.Configuration, configuration),
                navigator.Joined, ReferenceEquals(registry.Get<IPasskeyNavigator>(), navigator));
        })
        .TheTest
        .ShouldPass<ScreensOutcome>((because, outcome) =>
        {
            because.ItsTrue("the screens need the client first: " + outcome.WithoutClient, outcome.WithoutClient is not null && outcome.WithoutClient.Contains("AddPasskeyClient", StringComparison.Ordinal));
            because.ItsTrue("a null navigator is refused", outcome.NullNavigatorRefused);
            because.ItsTrue("one theme is shared", outcome.SharedTheme);
            because.ItsTrue("each screen gets its own view model", outcome.FreshViewModels);
            because.ItsTrue("the view models read the host's configuration", outcome.SignInConfiguration && outcome.RegistrationConfiguration);
            because.ItsTrue("a sign-in through the bound client reaches the host's navigator: " + outcome.NavigatorCalls, outcome.NavigatorCalls == "signedIn");
            because.ItsTrue("the navigator is the host's", outcome.SameNavigator);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private sealed record ScreensOutcome(string? WithoutClient, bool NullNavigatorRefused, bool SharedTheme, bool FreshViewModels, bool SignInConfiguration, bool RegistrationConfiguration,
        string NavigatorCalls, bool SameNavigator);

    private sealed record BindingOutcome(bool ClientResolved, bool SameConfiguration, bool DefaultAuthenticator, bool NormalizerResolved, PasskeyErrorCategory? SignInCategory, int BackendCalls);

    private sealed record HostBindingOutcome(bool KeptHostAuthenticator, bool SignedInBefore, bool SignedInAfter);

    private sealed record InvalidSetupOutcome(bool InvalidRefused, string? MissingBackendMessage);

    private sealed record BridgeOutcome(bool SignedIn, bool SameConfiguration, string ApplicationName);
}
