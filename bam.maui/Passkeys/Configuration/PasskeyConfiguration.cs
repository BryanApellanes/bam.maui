namespace Bam.Maui.Passkeys;

/// <summary>
/// Everything a host app sets to use the passkey library: which relying party it signs in to,
/// which operations are switched on, how the surrounding UI is branded and what it says.  One
/// instance is registered per app by <see cref="PasskeyClientRegistration.AddPasskeyClient"/>,
/// which calls <see cref="Validate"/>.
/// </summary>
public sealed class PasskeyConfiguration
{
    /// <summary>
    /// The relying-party id the app's passkeys belong to: a bare lowercase host name such as
    /// <c>example.com</c>.  It must match the backend's RP id and the domain that serves the
    /// app's association files (Digital Asset Links, Apple App Site Association).
    /// </summary>
    public string RelyingPartyId { get; set; } = string.Empty;

    /// <summary>Which operations the app offers.</summary>
    public PasskeyFeatures Features { get; set; } = new PasskeyFeatures();

    /// <summary>The brand values the library's views read.</summary>
    public PasskeyBranding Branding { get; set; } = new PasskeyBranding();

    /// <summary>The user-facing text for each failure; replace members to localize or reword.</summary>
    public PasskeyMessages Messages { get; set; } = new PasskeyMessages();

    /// <summary>
    /// Checks the configuration is coherent, including its features, branding and messages.
    /// </summary>
    /// <exception cref="ArgumentException">A value is missing or malformed; the message names it.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RelyingPartyId))
        {
            throw new ArgumentException("PasskeyConfiguration.RelyingPartyId is required.");
        }
        if (RelyingPartyId.Contains('/') || RelyingPartyId.Contains(':') || RelyingPartyId.Contains(' '))
        {
            throw new ArgumentException($"PasskeyConfiguration.RelyingPartyId '{RelyingPartyId}' must be a bare host name without scheme, port or path.");
        }
        if (!string.Equals(RelyingPartyId, RelyingPartyId.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException($"PasskeyConfiguration.RelyingPartyId '{RelyingPartyId}' must be lowercase.");
        }
        if (Features is null)
        {
            throw new ArgumentException("PasskeyConfiguration.Features is required.");
        }
        if (Branding is null)
        {
            throw new ArgumentException("PasskeyConfiguration.Branding is required.");
        }
        if (Messages is null)
        {
            throw new ArgumentException("PasskeyConfiguration.Messages is required.");
        }
        Features.Validate();
        Branding.Validate();
        Messages.Validate();
    }
}

/// <summary>
/// Switches for the operations an app offers.  A switched-off operation fails with
/// <see cref="PasskeyErrorCategory.FeatureDisabled"/> before the backend or the device is touched.
/// </summary>
public sealed class PasskeyFeatures
{
    /// <summary>Whether accounts can be created with a passkey. Default true.</summary>
    public bool EnableRegistration { get; set; } = true;

    /// <summary>Whether users can sign in with a passkey. Default true.</summary>
    public bool EnableAuthentication { get; set; } = true;

    /// <summary>Whether signed-in users can list, add and remove passkeys. Default true.</summary>
    public bool EnableManagement { get; set; } = true;

    /// <summary>Whether sign-in without a username (discoverable credentials) is offered. Default true.</summary>
    public bool EnableUsernameLessAuthentication { get; set; } = true;

    /// <summary>Checks the switches are coherent.</summary>
    /// <exception cref="ArgumentException">Nothing is enabled, or username-less sign-in is on while authentication is off.</exception>
    public void Validate()
    {
        if (!EnableRegistration && !EnableAuthentication && !EnableManagement)
        {
            throw new ArgumentException("PasskeyFeatures must enable at least one of registration, authentication or management.");
        }
        if (EnableUsernameLessAuthentication && !EnableAuthentication)
        {
            throw new ArgumentException("PasskeyFeatures.EnableUsernameLessAuthentication requires EnableAuthentication.");
        }
    }
}
