using System.Text.Json;

namespace Bam.Maui.Passkeys;

/// <summary>
/// One entry of <c>excludeCredentials</c> or <c>allowCredentials</c>.
/// </summary>
public sealed class WebAuthnCredentialDescriptor
{
    /// <summary>The only credential type WebAuthn defines.</summary>
    public const string PublicKeyType = "public-key";

    /// <summary>Creates a descriptor.</summary>
    /// <param name="id">The credential id bytes.</param>
    /// <param name="transports">The transport hints the relying party stored, possibly empty.</param>
    public WebAuthnCredentialDescriptor(byte[] id, IReadOnlyList<string> transports)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(transports);
        Id = id;
        Transports = transports;
    }

    /// <summary>The credential id bytes.</summary>
    public byte[] Id { get; }

    /// <summary>The transport hints, possibly empty.</summary>
    public IReadOnlyList<string> Transports { get; }

    /// <summary>Names the type only, so a credential id never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnCredentialDescriptor);
}

/// <summary>
/// The parts of <c>PublicKeyCredentialCreationOptionsJSON</c> a platform passkey API asks for as
/// separate arguments.  Built by <see cref="Parse"/>, which also checks the options are for this
/// app's relying party.
/// </summary>
public sealed class WebAuthnCreationRequest
{
    private WebAuthnCreationRequest(string relyingPartyId, string relyingPartyName, byte[] challenge, byte[] userId, string userName, string userDisplayName)
    {
        RelyingPartyId = relyingPartyId;
        RelyingPartyName = relyingPartyName;
        Challenge = challenge;
        UserId = userId;
        UserName = userName;
        UserDisplayName = userDisplayName;
    }

    /// <summary><c>rp.id</c>; equal to the configured relying party id.</summary>
    public string RelyingPartyId { get; }

    /// <summary><c>rp.name</c>; the relying party id when the options give none.</summary>
    public string RelyingPartyName { get; }

    /// <summary>The decoded <c>challenge</c>.</summary>
    public byte[] Challenge { get; }

    /// <summary>The decoded <c>user.id</c>, 1 to 64 bytes.</summary>
    public byte[] UserId { get; }

    /// <summary><c>user.name</c>.</summary>
    public string UserName { get; }

    /// <summary><c>user.displayName</c>; <see cref="UserName"/> when the options give none.</summary>
    public string UserDisplayName { get; }

    /// <summary>The COSE algorithm identifiers from <c>pubKeyCredParams</c>, in the relying party's order of preference.</summary>
    public IReadOnlyList<int> Algorithms { get; private init; } = Array.Empty<int>();

    /// <summary><c>timeout</c> in milliseconds, when given.</summary>
    public int? TimeoutMilliseconds { get; private init; }

    /// <summary><c>excludeCredentials</c>, possibly empty.</summary>
    public IReadOnlyList<WebAuthnCredentialDescriptor> ExcludeCredentials { get; private init; } = Array.Empty<WebAuthnCredentialDescriptor>();

    /// <summary><c>authenticatorSelection.authenticatorAttachment</c>, when given.</summary>
    public string? AuthenticatorAttachment { get; private init; }

    /// <summary>
    /// The resident key requirement: <c>authenticatorSelection.residentKey</c> when given, else
    /// <c>required</c> or <c>discouraged</c> from the older <c>requireResidentKey</c> flag.
    /// </summary>
    public string ResidentKey { get; private init; } = "discouraged";

    /// <summary><c>authenticatorSelection.userVerification</c>; <c>preferred</c> when the options give none.</summary>
    public string UserVerification { get; private init; } = "preferred";

    /// <summary><c>attestation</c>; <c>none</c> when the options give none.</summary>
    public string Attestation { get; private init; } = "none";

    /// <summary>Names the type only, so a challenge or user id never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnCreationRequest);

    /// <summary>
    /// Reads creation options and checks they are usable and meant for this app.
    /// </summary>
    /// <param name="options">The relying party's creation options.</param>
    /// <param name="relyingPartyId">The app's configured relying party id; <c>rp.id</c> must equal it.</param>
    /// <returns>The parsed request.</returns>
    /// <exception cref="PasskeyException"><see cref="PasskeyErrorCategory.InvalidOptions"/>: the JSON is malformed, a required member is missing, or <c>rp.id</c> is absent or names another relying party.</exception>
    public static WebAuthnCreationRequest Parse(WebAuthnCreationOptions options, string relyingPartyId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(relyingPartyId);
        using JsonDocument document = WebAuthnOptionsJson.Open(options.Json, "The creation options");
        JsonElement root = document.RootElement;

        JsonElement rp = WebAuthnOptionsJson.RequiredObject(root, "rp");
        string? rpId = WebAuthnOptionsJson.OptionalString(rp, "id", "rp.id");
        WebAuthnOptionsJson.RequireRelyingParty(rpId, relyingPartyId, "rp.id");

        JsonElement user = WebAuthnOptionsJson.RequiredObject(root, "user");
        string userName = WebAuthnOptionsJson.RequiredString(user, "name", "user.name");

        string? attachment = null;
        string residentKey = "discouraged";
        string userVerification = "preferred";
        if (root.TryGetProperty("authenticatorSelection", out JsonElement selection) && selection.ValueKind == JsonValueKind.Object)
        {
            attachment = WebAuthnOptionsJson.OptionalString(selection, "authenticatorAttachment", "authenticatorSelection.authenticatorAttachment");
            userVerification = WebAuthnOptionsJson.OptionalString(selection, "userVerification", "authenticatorSelection.userVerification") ?? userVerification;
            bool required = selection.TryGetProperty("requireResidentKey", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
            residentKey = WebAuthnOptionsJson.OptionalString(selection, "residentKey", "authenticatorSelection.residentKey") ?? (required ? "required" : "discouraged");
        }

        return new WebAuthnCreationRequest(
            relyingPartyId,
            WebAuthnOptionsJson.OptionalString(rp, "name", "rp.name") ?? relyingPartyId,
            WebAuthnOptionsJson.RequiredBytes(root, "challenge", "challenge", 1024),
            WebAuthnOptionsJson.RequiredBytes(user, "id", "user.id", 64),
            userName,
            WebAuthnOptionsJson.OptionalString(user, "displayName", "user.displayName") ?? userName)
        {
            Algorithms = ReadAlgorithms(root),
            TimeoutMilliseconds = WebAuthnOptionsJson.OptionalTimeout(root),
            ExcludeCredentials = WebAuthnOptionsJson.Descriptors(root, "excludeCredentials"),
            AuthenticatorAttachment = attachment,
            ResidentKey = residentKey,
            UserVerification = userVerification,
            Attestation = WebAuthnOptionsJson.OptionalString(root, "attestation", "attestation") ?? "none"
        };
    }

    private const int EcdsaSha256 = -7;
    private const int RsaSha256 = -257;

    private static IReadOnlyList<int> ReadAlgorithms(JsonElement root)
    {
        if (!root.TryGetProperty("pubKeyCredParams", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            throw WebAuthnOptionsJson.Invalid("The options have no 'pubKeyCredParams' array.");
        }
        if (list.GetArrayLength() == 0)
        {
            // WebAuthn: an empty list means ES256 and RS256.
            return new List<int> { EcdsaSha256, RsaSha256 };
        }
        List<int> algorithms = new List<int>();
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw WebAuthnOptionsJson.Invalid("'pubKeyCredParams' must hold objects.");
            }
            if (!string.Equals(WebAuthnOptionsJson.OptionalString(item, "type", "pubKeyCredParams.type"), WebAuthnCredentialDescriptor.PublicKeyType, StringComparison.Ordinal))
            {
                continue;
            }
            if (!item.TryGetProperty("alg", out JsonElement alg) || alg.ValueKind != JsonValueKind.Number || !alg.TryGetInt32(out int identifier))
            {
                throw WebAuthnOptionsJson.Invalid("'pubKeyCredParams.alg' must be a whole number.");
            }
            algorithms.Add(identifier);
        }
        if (algorithms.Count == 0)
        {
            throw WebAuthnOptionsJson.Invalid("'pubKeyCredParams' names no public-key algorithm.");
        }
        return algorithms;
    }
}

/// <summary>
/// The parts of <c>PublicKeyCredentialRequestOptionsJSON</c> a platform passkey API asks for as
/// separate arguments.  Built by <see cref="Parse"/>, which also checks the options are for this
/// app's relying party.
/// </summary>
public sealed class WebAuthnAssertionRequest
{
    private WebAuthnAssertionRequest(string relyingPartyId, byte[] challenge)
    {
        RelyingPartyId = relyingPartyId;
        Challenge = challenge;
    }

    /// <summary><c>rpId</c>; equal to the configured relying party id.</summary>
    public string RelyingPartyId { get; }

    /// <summary>The decoded <c>challenge</c>.</summary>
    public byte[] Challenge { get; }

    /// <summary><c>timeout</c> in milliseconds, when given.</summary>
    public int? TimeoutMilliseconds { get; private init; }

    /// <summary><c>allowCredentials</c>; empty for a username-less sign-in.</summary>
    public IReadOnlyList<WebAuthnCredentialDescriptor> AllowCredentials { get; private init; } = Array.Empty<WebAuthnCredentialDescriptor>();

    /// <summary><c>userVerification</c>; <c>preferred</c> when the options give none.</summary>
    public string UserVerification { get; private init; } = "preferred";

    /// <summary>Names the type only, so a challenge never lands in a log through string formatting.</summary>
    public override string ToString() => nameof(WebAuthnAssertionRequest);

    /// <summary>
    /// Reads request options and checks they are usable and meant for this app.
    /// </summary>
    /// <param name="options">The relying party's request options.</param>
    /// <param name="relyingPartyId">The app's configured relying party id; <c>rpId</c> must equal it.</param>
    /// <returns>The parsed request.</returns>
    /// <exception cref="PasskeyException"><see cref="PasskeyErrorCategory.InvalidOptions"/>: the JSON is malformed, the challenge is missing, or <c>rpId</c> is absent or names another relying party.</exception>
    public static WebAuthnAssertionRequest Parse(WebAuthnRequestOptions options, string relyingPartyId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(relyingPartyId);
        using JsonDocument document = WebAuthnOptionsJson.Open(options.Json, "The request options");
        JsonElement root = document.RootElement;

        string? rpId = WebAuthnOptionsJson.OptionalString(root, "rpId", "rpId");
        WebAuthnOptionsJson.RequireRelyingParty(rpId, relyingPartyId, "rpId");

        return new WebAuthnAssertionRequest(relyingPartyId, WebAuthnOptionsJson.RequiredBytes(root, "challenge", "challenge", 1024))
        {
            TimeoutMilliseconds = WebAuthnOptionsJson.OptionalTimeout(root),
            AllowCredentials = WebAuthnOptionsJson.Descriptors(root, "allowCredentials"),
            UserVerification = WebAuthnOptionsJson.OptionalString(root, "userVerification", "userVerification") ?? "preferred"
        };
    }
}
