namespace Bam.Maui.Passkeys;

/// <summary>
/// Whether a passkey ceremony can run on this device, and the reason when it cannot.
/// </summary>
public enum PasskeyAvailability
{
    /// <summary>The platform has no passkey implementation in this library (the default until one is registered).</summary>
    NotSupported = 0,

    /// <summary>A ceremony can run.</summary>
    Available,

    /// <summary>The platform is supported but this operating system version is too old.</summary>
    UnsupportedOperatingSystem,

    /// <summary>No credential provider is installed or enabled (for example no password manager on Android).</summary>
    MissingCredentialProvider,

    /// <summary>The device has no screen lock, biometric or PIN configured.</summary>
    DeviceSecurityNotConfigured
}

/// <summary>
/// What the device's passkey support looks like right now, as reported by the
/// <see cref="IPasskeyAuthenticator"/>.
/// </summary>
public sealed record PasskeyCapabilities
{
    /// <summary>Whether a ceremony can run, and why not.</summary>
    public PasskeyAvailability Availability { get; init; } = PasskeyAvailability.NotSupported;

    /// <summary>Whether username-less sign-in with discoverable credentials is possible.</summary>
    public bool SupportsDiscoverableCredentials { get; init; }

    /// <summary>Whether external FIDO2 security keys can be used.</summary>
    public bool SupportsSecurityKeys { get; init; }

    /// <summary>The platform the report describes (for example <c>Android</c>, <c>iOS</c>, <c>Windows</c>).</summary>
    public string Platform { get; init; } = "Unknown";

    /// <summary>True only when a ceremony can run.</summary>
    public bool IsSupported => Availability == PasskeyAvailability.Available;

    /// <summary>
    /// The error category a caller should report when a ceremony is refused because of these
    /// capabilities; <see cref="PasskeyErrorCategory.None"/> when the device is supported.
    /// </summary>
    /// <returns>The matching category.</returns>
    public PasskeyErrorCategory ToErrorCategory()
    {
        return Availability switch
        {
            PasskeyAvailability.Available => PasskeyErrorCategory.None,
            PasskeyAvailability.MissingCredentialProvider => PasskeyErrorCategory.MissingCredentialProvider,
            PasskeyAvailability.DeviceSecurityNotConfigured => PasskeyErrorCategory.DeviceSecurityNotConfigured,
            _ => PasskeyErrorCategory.UnsupportedOperatingSystem
        };
    }

    /// <summary>Capabilities for a platform that cannot run passkey ceremonies.</summary>
    /// <param name="platform">The platform name to report.</param>
    /// <returns>Capabilities with <see cref="PasskeyAvailability.NotSupported"/>.</returns>
    public static PasskeyCapabilities Unsupported(string platform = "Unknown")
    {
        return new PasskeyCapabilities { Availability = PasskeyAvailability.NotSupported, Platform = platform };
    }
}
