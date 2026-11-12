using Bam.DependencyInjection;
using Bam.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Registers the passkey client.  A host calls <see cref="AddPasskeyClient"/> on its bam
/// <see cref="ServiceRegistry"/>, or <see cref="AddBamPasskeys"/> on the MAUI service collection
/// to do that and make <see cref="IPasskeyClient"/> injectable into pages and view models.
/// </summary>
public static class PasskeyClientRegistration
{
    /// <summary>
    /// Validates the configuration and binds it, the <see cref="PasskeyErrorNormalizer"/> and
    /// <see cref="IPasskeyClient"/> (<see cref="PasskeyClient"/>).
    /// <para>
    /// <see cref="IPasskeyAuthenticator"/> is bound to the platform's own authenticator
    /// (<see cref="PasskeyPlatform.CreateAuthenticator"/>) only when the host has not bound one,
    /// so an authenticator the host registers before or after this call wins.  <see cref="IPasskeyBackend"/> is the host's to bind and must be
    /// bound by the time <see cref="IPasskeyClient"/> is resolved.  A bound
    /// <see cref="ILogger"/> is used for failure logging; without one nothing is logged.
    /// </para>
    /// </summary>
    /// <param name="registry">The host registry.</param>
    /// <param name="configuration">The app's passkey configuration; validated here.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentException">The configuration is invalid (see <see cref="PasskeyConfiguration.Validate"/>).</exception>
    public static ServiceRegistry AddPasskeyClient(this ServiceRegistry registry, PasskeyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();

        PasskeyErrorNormalizer normalizer = new PasskeyErrorNormalizer(configuration.Messages);
        registry.For<PasskeyConfiguration>().UseSingleton(configuration);
        registry.For<PasskeyErrorNormalizer>().UseSingleton(normalizer);
        if (!registry.Contains<IPasskeyAuthenticator>())
        {
            registry.For<IPasskeyAuthenticator>().UseSingleton<IPasskeyAuthenticator>(PasskeyPlatform.CreateAuthenticator(configuration));
        }
        registry.For<IPasskeyClient>().Use<IPasskeyClient>(resolver => CreateClient(resolver, configuration, normalizer));
        return registry;
    }

    /// <summary>
    /// Runs <see cref="AddPasskeyClient"/> on the bam registry and bridges the result into the
    /// MAUI service collection: the configuration and branding as singletons, and
    /// <see cref="IPasskeyClient"/> resolved from the registry on each request, so nothing is
    /// registered twice and the registry stays the single source of bindings.
    /// </summary>
    /// <param name="services">The MAUI app's service collection (<c>builder.Services</c>).</param>
    /// <param name="registry">The host's bam registry, holding its <see cref="IPasskeyBackend"/> and platform <see cref="IPasskeyAuthenticator"/>.</param>
    /// <param name="configuration">The app's passkey configuration; validated here.</param>
    /// <returns>The service collection.</returns>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    public static IServiceCollection AddBamPasskeys(this IServiceCollection services, ServiceRegistry registry, PasskeyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        registry.AddPasskeyClient(configuration);

        services.AddSingleton(configuration);
        services.AddSingleton(configuration.Branding);
        services.AddTransient<IPasskeyClient>(_ => registry.Get<IPasskeyClient>());
        return services;
    }

    private static IPasskeyClient CreateClient(ServiceRegistry registry, PasskeyConfiguration configuration, PasskeyErrorNormalizer normalizer)
    {
        if (!registry.Contains<IPasskeyBackend>())
        {
            throw new InvalidOperationException("No IPasskeyBackend is registered. Bind the app's relying-party backend on the ServiceRegistry before resolving IPasskeyClient.");
        }
        ILogger? logger = registry.Contains<ILogger>() ? registry.Get<ILogger>() : null;
        return new PasskeyClient(registry.Get<IPasskeyBackend>(), registry.Get<IPasskeyAuthenticator>(), configuration, normalizer, logger);
    }
}
