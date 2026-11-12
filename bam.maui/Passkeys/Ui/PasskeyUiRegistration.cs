using Bam.DependencyInjection;
using Bam.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Binds the passkey screens: the app's <see cref="IPasskeyNavigator"/>, one shared <see cref="PasskeyTheme"/>, and the
/// view models, views and pages, each new per resolution.  Call it after
/// <see cref="PasskeyClientRegistration.AddPasskeyClient"/>, which binds the configuration and the client the screens use.
/// </summary>
public static class PasskeyUiRegistration
{
    /// <summary>Binds the screens in a bam registry.</summary>
    /// <param name="registry">The host registry, already holding the passkey client.</param>
    /// <param name="navigator">The app's navigation.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="InvalidOperationException">The passkey client isn't bound yet.</exception>
    public static ServiceRegistry AddPasskeyUi(this ServiceRegistry registry, IPasskeyNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(navigator);
        if (!registry.Contains<PasskeyConfiguration>() || !registry.Contains<IPasskeyClient>())
        {
            throw new InvalidOperationException("Call AddPasskeyClient before AddPasskeyUi: the screens need the passkey configuration and client.");
        }
        PasskeyConfiguration configuration = registry.Get<PasskeyConfiguration>();
        ILogger? logger = registry.Contains<ILogger>() ? registry.Get<ILogger>() : null;
        registry.For<IPasskeyNavigator>().UseSingleton(navigator);
        registry.For<PasskeyTheme>().UseSingleton(new PasskeyTheme(configuration.Branding));
        registry.For<PasskeySignInViewModel>().Use<PasskeySignInViewModel>(r => new PasskeySignInViewModel(r.Get<IPasskeyClient>(), configuration, r.Get<IPasskeyNavigator>(), logger));
        registry.For<PasskeyRegistrationViewModel>().Use<PasskeyRegistrationViewModel>(r => new PasskeyRegistrationViewModel(r.Get<IPasskeyClient>(), configuration, r.Get<IPasskeyNavigator>(), logger));
        registry.For<PasskeySignInView>().Use<PasskeySignInView>(r => new PasskeySignInView(r.Get<PasskeySignInViewModel>(), r.Get<PasskeyTheme>()));
        registry.For<PasskeyRegistrationView>().Use<PasskeyRegistrationView>(r => new PasskeyRegistrationView(r.Get<PasskeyRegistrationViewModel>(), r.Get<PasskeyTheme>()));
        registry.For<PasskeySignInPage>().Use<PasskeySignInPage>(r => new PasskeySignInPage(r.Get<PasskeySignInView>()));
        registry.For<PasskeyRegistrationPage>().Use<PasskeyRegistrationPage>(r => new PasskeyRegistrationPage(r.Get<PasskeyRegistrationView>()));
        return registry;
    }

    /// <summary>
    /// Binds the screens in the bam registry and exposes them to MAUI's service collection, so pages can be resolved from
    /// <c>builder.Services</c> or constructor-injected.
    /// </summary>
    /// <param name="services">The MAUI app's service collection.</param>
    /// <param name="registry">The host registry, already holding the passkey client (see <see cref="PasskeyClientRegistration.AddBamPasskeys"/>).</param>
    /// <param name="navigator">The app's navigation.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddBamPasskeyUi(this IServiceCollection services, ServiceRegistry registry, IPasskeyNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(services);
        registry.AddPasskeyUi(navigator);
        services.AddSingleton(registry.Get<PasskeyTheme>());
        services.AddSingleton(navigator);
        services.AddTransient(_ => registry.Get<PasskeySignInViewModel>());
        services.AddTransient(_ => registry.Get<PasskeyRegistrationViewModel>());
        services.AddTransient(_ => registry.Get<PasskeySignInView>());
        services.AddTransient(_ => registry.Get<PasskeyRegistrationView>());
        services.AddTransient(_ => registry.Get<PasskeySignInPage>());
        services.AddTransient(_ => registry.Get<PasskeyRegistrationPage>());
        return services;
    }
}
