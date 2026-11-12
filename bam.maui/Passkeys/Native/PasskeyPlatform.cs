namespace Bam.Maui.Passkeys;

/// <summary>
/// Picks the passkey authenticator for the platform the app was built for.
/// </summary>
public static class PasskeyPlatform
{
    /// <summary>
    /// Creates the platform's authenticator: <c>AndroidPasskeyAuthenticator</c> on Android,
    /// <c>ApplePasskeyAuthenticator</c> on iOS and Mac Catalyst, <c>WindowsPasskeyAuthenticator</c>
    /// on Windows.  A build for the plain <c>net10.0</c> target has no app window to prompt over
    /// and gets <see cref="UnsupportedPasskeyAuthenticator"/>; a host on that target that has a
    /// window constructs <c>WindowsPasskeyAuthenticator</c> itself.
    /// </summary>
    /// <param name="configuration">The app's passkey configuration.</param>
    /// <returns>The authenticator <see cref="PasskeyClientRegistration.AddPasskeyClient"/> binds by default.</returns>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    public static IPasskeyAuthenticator CreateAuthenticator(PasskeyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
#if ANDROID
        return new AndroidPasskeyAuthenticator(configuration);
#elif IOS || MACCATALYST
        return new ApplePasskeyAuthenticator(configuration);
#elif WINDOWS
        return new WindowsPasskeyAuthenticator(configuration, MauiWindowHandle);
#else
        return new UnsupportedPasskeyAuthenticator();
#endif
    }

#if WINDOWS
    // The handle of the app's first window, or zero before one exists.
    private static IntPtr MauiWindowHandle()
    {
        Microsoft.Maui.Controls.Window? window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            return WinRT.Interop.WindowNative.GetWindowHandle(native);
        }
        return IntPtr.Zero;
    }
#endif
}
