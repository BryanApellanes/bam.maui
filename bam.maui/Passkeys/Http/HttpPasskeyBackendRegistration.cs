using Bam.DependencyInjection;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Binds <see cref="HttpPasskeyBackend"/> as the app's <see cref="IPasskeyBackend"/>.
/// </summary>
public static class HttpPasskeyBackendRegistration
{
    /// <summary>
    /// Binds a <see cref="HttpPasskeyBackend"/> built by <see cref="HttpPasskeyBackend.Create(PasskeyConfiguration, PasskeyHttpBackendOptions?)"/>
    /// as the singleton <see cref="IPasskeyBackend"/>, unless the host already bound one.  Call it
    /// before resolving <see cref="IPasskeyClient"/>; it works before or after
    /// <see cref="PasskeyClientRegistration.AddPasskeyClient"/>.
    /// </summary>
    /// <param name="registry">The host registry.</param>
    /// <param name="configuration">The app's passkey configuration; validated here.</param>
    /// <param name="options">Where the account endpoints are; null for <c>https://&lt;relying party id&gt;/account/</c>.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentException">The configuration or an option is invalid.</exception>
    public static ServiceRegistry AddHttpPasskeyBackend(this ServiceRegistry registry, PasskeyConfiguration configuration, PasskeyHttpBackendOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!registry.Contains<IPasskeyBackend>())
        {
            registry.For<IPasskeyBackend>().UseSingleton<IPasskeyBackend>(HttpPasskeyBackend.Create(configuration, options));
        }
        return registry;
    }
}
