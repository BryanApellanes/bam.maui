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

    private sealed record BindingOutcome(bool ClientResolved, bool SameConfiguration, bool DefaultAuthenticator, bool NormalizerResolved, PasskeyErrorCategory? SignInCategory, int BackendCalls);

    private sealed record HostBindingOutcome(bool KeptHostAuthenticator, bool SignedInBefore, bool SignedInAfter);

    private sealed record InvalidSetupOutcome(bool InvalidRefused, string? MissingBackendMessage);

    private sealed record BridgeOutcome(bool SignedIn, bool SameConfiguration, string ApplicationName);
}
