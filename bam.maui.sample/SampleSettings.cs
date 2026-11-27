using System.Text.Json;
using Bam.Maui.Passkeys;

namespace Bam.Maui.Sample;

/// <summary>
/// What the sample app reads from its bundled <c>samplesettings.json</c> (the build embeds the level's <c>samplesettings.&lt;level&gt;.json</c> under that name): the relying party it signs in to, where the
/// account endpoints are, and the brand it wears.  Everything brand-specific in the app comes from here, so a second
/// brand is a second settings file, not a code change.  Unknown members are refused, so a misspelt key fails at start-up
/// instead of silently falling back to the default.
/// </summary>
public sealed class SampleSettings
{
    /// <summary>The name of the bundled settings resource.</summary>
    public const string ResourceName = "samplesettings.json";

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        // A key given twice is refused rather than the last one silently winning.
        AllowDuplicateProperties = false,
        // themeMode reads as its name ("Dark"); numbers, and names that aren't defined, are refused.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>The relying-party id; <c>whoiam.is</c>, the default brand, when the file leaves it out.</summary>
    public string RelyingPartyId { get; set; } = "whoiam.is";

    /// <summary>The account endpoint base; null for <c>https://&lt;relying party id&gt;/account/</c>.</summary>
    public string? AccountEndpoint { get; set; }

    /// <summary>The brand values the passkey screens read.</summary>
    public PasskeyBranding Branding { get; set; } = new PasskeyBranding { ApplicationName = "Passkey Sample" };

    /// <summary>Whether sign-up asks for an email address.</summary>
    public bool AskForEmail { get; set; }

    /// <summary>Reads settings from JSON.</summary>
    /// <param name="json">The settings document.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentException">The document isn't valid settings JSON.</exception>
    public static SampleSettings Read(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            SampleSettings? settings = JsonSerializer.Deserialize<SampleSettings>(json, Json);
            return settings ?? throw new ArgumentException(ResourceName + " is empty.");
        }
        catch (JsonException exception)
        {
            // The parser's message names the member and position, never a value.
            throw new ArgumentException(ResourceName + " is not valid: " + exception.Message, exception);
        }
    }

    /// <summary>Reads the settings bundled with the app.</summary>
    /// <returns>The settings.</returns>
    /// <exception cref="InvalidOperationException">The resource is missing from the build.</exception>
    public static SampleSettings ReadBundled()
    {
        using Stream? stream = typeof(SampleSettings).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(ResourceName + " is not embedded in the app.");
        }
        return Read(stream);
    }

    /// <summary>The passkey configuration these settings describe, validated.</summary>
    /// <returns>The configuration.</returns>
    /// <exception cref="ArgumentException">A value is missing or malformed.</exception>
    public PasskeyConfiguration ToConfiguration()
    {
        PasskeyConfiguration configuration = new PasskeyConfiguration
        {
            RelyingPartyId = RelyingPartyId,
            Branding = Branding,
            Experience = new PasskeyExperience { AskForEmail = AskForEmail },
        };
        configuration.Validate();
        return configuration;
    }

    /// <summary>The account endpoint options these settings describe.</summary>
    /// <returns>The options; the backend checks them against the relying party when it is created.</returns>
    /// <exception cref="ArgumentException">The endpoint isn't an absolute URI.</exception>
    public PasskeyHttpBackendOptions ToBackendOptions()
    {
        if (AccountEndpoint is null)
        {
            return new PasskeyHttpBackendOptions();
        }
        if (!Uri.TryCreate(AccountEndpoint, UriKind.Absolute, out Uri? endpoint))
        {
            throw new ArgumentException("accountEndpoint must be an absolute https URI.");
        }
        return new PasskeyHttpBackendOptions { AccountEndpoint = endpoint };
    }
}
