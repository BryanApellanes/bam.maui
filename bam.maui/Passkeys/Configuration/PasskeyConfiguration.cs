using System.Text.RegularExpressions;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Everything a host app sets to use the passkey library: which relying party it signs in to,
/// which operations are switched on, how the surrounding UI is branded and what it says.  One
/// instance is registered per app by <see cref="PasskeyClientRegistration.AddPasskeyClient"/>,
/// which calls <see cref="Validate"/>.
/// </summary>
public sealed class PasskeyConfiguration
{
    // Dot-separated LDH labels, each 1-63 characters and not starting or ending with a hyphen; 253 characters overall.
    private static readonly Regex HostName = new Regex(@"\A(?=.{1,253}\z)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// The relying-party id the app's passkeys belong to: a bare lowercase host name such as
    /// <c>example.com</c> (an internationalized name in its punycode form).  It must match the backend's RP id and the domain that serves the
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
        if (!HostName.IsMatch(RelyingPartyId) || PasskeyHostNames.LastLabelIsNumeric(RelyingPartyId))
        {
            // The value is not echoed: it may hold control characters, which is one of the things being refused.
            throw new ArgumentException("PasskeyConfiguration.RelyingPartyId must be a bare lowercase DNS host name (letters, digits and hyphens in dot-separated labels) with no scheme, port, path, whitespace or trailing dot, and not an IP address.");
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

internal static class PasskeyHostNames
{
    // A WebAuthn RP id is a domain, never an address: a name whose last label is all digits is an IPv4 address or a bare number.
    internal static bool LastLabelIsNumeric(string host)
    {
        string last = host.Substring(host.LastIndexOf('.') + 1);
        return last.Length > 0 && last.All(char.IsAsciiDigit);
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
