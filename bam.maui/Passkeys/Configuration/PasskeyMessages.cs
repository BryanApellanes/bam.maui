namespace Bam.Maui.Passkeys;

/// <summary>
/// The text a user sees for each kind of failure, with English defaults.  A host replaces members
/// to reword or localize (assign from its own resources at startup).  These are the only strings
/// that reach a <see cref="PasskeyError"/>; exception text never does.
/// </summary>
public sealed class PasskeyMessages
{
    /// <summary>Shown when the user dismissed the prompt.</summary>
    public string UserCancelled { get; set; } = "That was cancelled.";

    /// <summary>Shown when the device holds no passkey that fits.</summary>
    public string NoCredentials { get; set; } = "No passkey for this account was found on this device.";

    /// <summary>Shown when the platform or OS version cannot run passkeys.</summary>
    public string UnsupportedOperatingSystem { get; set; } = "Passkeys aren't available on this device.";

    /// <summary>Shown when no credential provider is set up.</summary>
    public string MissingCredentialProvider { get; set; } = "Set up a password manager on this device to use passkeys.";

    /// <summary>Shown when the device has no screen lock.</summary>
    public string DeviceSecurityNotConfigured { get; set; } = "Set up a screen lock on this device to use passkeys.";

    /// <summary>Shown when the backend could not be reached.</summary>
    public string NetworkFailure { get; set; } = "Couldn't reach the server. Check your connection and try again.";

    /// <summary>Shown when the backend's answer was not usable.</summary>
    public string InvalidServerResponse { get; set; } = "The server sent an unexpected response. Try again.";

    /// <summary>Shown when the request could not be made as given.</summary>
    public string InvalidOptions { get; set; } = "That request couldn't be completed. Try again.";

    /// <summary>Shown when the challenge timed out.</summary>
    public string ExpiredChallenge { get; set; } = "That took too long. Try again.";

    /// <summary>Shown when the backend refused the ceremony.</summary>
    public string ServerVerificationFailed { get; set; } = "The passkey couldn't be verified. Try again or use another way to sign in.";

    /// <summary>Shown when the platform passkey API failed.</summary>
    public string PlatformFailure { get; set; } = "Something went wrong with the passkey on this device. Try again.";

    /// <summary>Shown when the operation is switched off for this app.</summary>
    public string FeatureDisabled { get; set; } = "This isn't available in this app.";

    /// <summary>Shown when the device already holds a passkey for the account.</summary>
    public string CredentialAlreadyRegistered { get; set; } = "This device already has a passkey for this account.";

    /// <summary>Shown for <see cref="PasskeyErrorCategory.NotSignedIn"/>.</summary>
    public string NotSignedIn { get; set; } = "Sign in again to continue.";

    /// <summary>Shown for <see cref="PasskeyErrorCategory.RateLimited"/>.</summary>
    public string RateLimited { get; set; } = "Too many attempts. Wait a moment and try again.";

    /// <summary>Shown for anything else.</summary>
    public string Unknown { get; set; } = "Something went wrong. Try again.";

    /// <summary>The message for a category.</summary>
    /// <param name="category">The failure category.</param>
    /// <returns>The configured message; <see cref="Unknown"/> for a category without one.</returns>
    public string For(PasskeyErrorCategory category)
    {
        return category switch
        {
            PasskeyErrorCategory.UserCancelled => UserCancelled,
            PasskeyErrorCategory.NoCredentials => NoCredentials,
            PasskeyErrorCategory.UnsupportedOperatingSystem => UnsupportedOperatingSystem,
            PasskeyErrorCategory.MissingCredentialProvider => MissingCredentialProvider,
            PasskeyErrorCategory.DeviceSecurityNotConfigured => DeviceSecurityNotConfigured,
            PasskeyErrorCategory.NetworkFailure => NetworkFailure,
            PasskeyErrorCategory.InvalidServerResponse => InvalidServerResponse,
            PasskeyErrorCategory.InvalidOptions => InvalidOptions,
            PasskeyErrorCategory.ExpiredChallenge => ExpiredChallenge,
            PasskeyErrorCategory.ServerVerificationFailed => ServerVerificationFailed,
            PasskeyErrorCategory.PlatformFailure => PlatformFailure,
            PasskeyErrorCategory.FeatureDisabled => FeatureDisabled,
            PasskeyErrorCategory.CredentialAlreadyRegistered => CredentialAlreadyRegistered,
            PasskeyErrorCategory.NotSignedIn => NotSignedIn,
            PasskeyErrorCategory.RateLimited => RateLimited,
            _ => Unknown
        };
    }

    /// <summary>Checks no message is blank, so a failure never shows an empty screen.</summary>
    /// <exception cref="ArgumentException">A message is null or blank; the message names the category.</exception>
    public void Validate()
    {
        foreach (PasskeyErrorCategory category in Enum.GetValues<PasskeyErrorCategory>())
        {
            if (category == PasskeyErrorCategory.None)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(For(category)))
            {
                throw new ArgumentException($"PasskeyMessages.{category} must not be blank.");
            }
        }
    }
}
