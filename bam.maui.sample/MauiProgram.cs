using Bam.DependencyInjection;
using Bam.Maui.Passkeys;

namespace Bam.Maui.Sample;

/// <summary>Builds the sample app: settings, then the passkey client and screens, then the navigation between them.</summary>
public static class MauiProgram
{
    /// <summary>Creates the app.</summary>
    /// <returns>The MAUI app.</returns>
    public static MauiApp CreateMauiApp()
    {
        SampleSettings settings = SampleSettings.ReadBundled();
        PasskeyConfiguration configuration = settings.ToConfiguration();

        ServiceRegistry registry = new ServiceRegistry();
        registry.AddHttpPasskeyBackend(configuration, settings.ToBackendOptions());
        SampleNavigator navigator = new SampleNavigator(registry, CurrentWindow);

        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<SampleApp>();
        builder.Services.AddBamPasskeys(registry, configuration);
        builder.Services.AddBamPasskeyUi(registry, navigator);
        builder.Services.AddSingleton(navigator);
        return builder.Build();
    }

    private static Window? CurrentWindow()
    {
        // Bam.Application is also a namespace, so MAUI's Application is named in full.
        IReadOnlyList<Window>? windows = Microsoft.Maui.Controls.Application.Current?.Windows;
        return windows is { Count: > 0 } ? windows[0] : null;
    }
}
