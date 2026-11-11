using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Writes the standard WebAuthn response JSON from the raw bytes a platform API returns, for the
/// platforms that do not produce that JSON themselves (Apple and Windows).
/// </summary>
public static class WebAuthnResponseJson
{
    /// <summary>The <c>authenticatorAttachment</c> of a credential held by the device itself.</summary>
    public const string PlatformAttachment = "platform";

    /// <summary>The <c>authenticatorAttachment</c> of a credential on a security key or another device.</summary>
    public const string CrossPlatformAttachment = "cross-platform";

    /// <summary>
    /// Writes a <c>RegistrationResponseJSON</c>.  The optional <c>publicKey</c> and
    /// <c>publicKeyAlgorithm</c> members are left out: the relying party reads the key from the
    /// attestation object, which is the copy the authenticator signed.
    /// </summary>
    /// <param name="credentialId">The new credential's id.</param>
    /// <param name="clientDataJson">The client data the authenticator signed over, as UTF-8 JSON.</param>
    /// <param name="attestationObject">The CBOR attestation object.</param>
    /// <param name="transports">How the credential can be reached later; may be empty when the platform does not say.</param>
    /// <param name="authenticatorAttachment"><see cref="PlatformAttachment"/>, <see cref="CrossPlatformAttachment"/>, or null when the platform does not say.</param>
    /// <returns>The registration response.</returns>
    /// <exception cref="PasskeyException"><see cref="PasskeyErrorCategory.PlatformFailure"/>: the platform returned an empty id, client data or attestation object.</exception>
    public static WebAuthnRegistrationResponse Registration(byte[]? credentialId, byte[]? clientDataJson, byte[]? attestationObject, IReadOnlyList<string> transports, string? authenticatorAttachment)
    {
        ArgumentNullException.ThrowIfNull(transports);
        byte[] id = Require(credentialId, "credential id");
        byte[] clientData = Require(clientDataJson, "client data");
        byte[] attestation = Require(attestationObject, "attestation object");

        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
        {
            WriteHead(writer, id, authenticatorAttachment);
            writer.WriteStartObject("response");
            writer.WriteString("clientDataJSON", Base64Url.EncodeToString(clientData));
            writer.WriteString("attestationObject", Base64Url.EncodeToString(attestation));
            writer.WriteStartArray("transports");
            foreach (string transport in transports)
            {
                writer.WriteStringValue(transport);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            WriteTail(writer);
        }
        return new WebAuthnRegistrationResponse(Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>Writes an <c>AuthenticationResponseJSON</c>.</summary>
    /// <param name="credentialId">The id of the credential that signed.</param>
    /// <param name="clientDataJson">The client data the authenticator signed over, as UTF-8 JSON.</param>
    /// <param name="authenticatorData">The authenticator data.</param>
    /// <param name="signature">The assertion signature.</param>
    /// <param name="userHandle">The user handle the credential carries; null or empty when the authenticator returned none.</param>
    /// <param name="authenticatorAttachment"><see cref="PlatformAttachment"/>, <see cref="CrossPlatformAttachment"/>, or null when the platform does not say.</param>
    /// <returns>The assertion response.</returns>
    /// <exception cref="PasskeyException"><see cref="PasskeyErrorCategory.PlatformFailure"/>: the platform returned an empty id, client data, authenticator data or signature.</exception>
    public static WebAuthnAssertionResponse Assertion(byte[]? credentialId, byte[]? clientDataJson, byte[]? authenticatorData, byte[]? signature, byte[]? userHandle, string? authenticatorAttachment)
    {
        byte[] id = Require(credentialId, "credential id");
        byte[] clientData = Require(clientDataJson, "client data");
        byte[] data = Require(authenticatorData, "authenticator data");
        byte[] signed = Require(signature, "signature");

        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
        {
            WriteHead(writer, id, authenticatorAttachment);
            writer.WriteStartObject("response");
            writer.WriteString("clientDataJSON", Base64Url.EncodeToString(clientData));
            writer.WriteString("authenticatorData", Base64Url.EncodeToString(data));
            writer.WriteString("signature", Base64Url.EncodeToString(signed));
            if (userHandle is not null && userHandle.Length > 0)
            {
                writer.WriteString("userHandle", Base64Url.EncodeToString(userHandle));
            }
            writer.WriteEndObject();
            WriteTail(writer);
        }
        return new WebAuthnAssertionResponse(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteHead(Utf8JsonWriter writer, byte[] credentialId, string? authenticatorAttachment)
    {
        string id = Base64Url.EncodeToString(credentialId);
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("rawId", id);
        writer.WriteString("type", WebAuthnCredentialDescriptor.PublicKeyType);
        if (authenticatorAttachment is not null)
        {
            writer.WriteString("authenticatorAttachment", authenticatorAttachment);
        }
    }

    private static void WriteTail(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("clientExtensionResults");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static byte[] Require(byte[]? bytes, string what)
    {
        if (bytes is null || bytes.Length == 0)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, $"The platform returned no {what}.");
        }
        return bytes;
    }
}

/// <summary>
/// Writes <c>clientDataJSON</c> for a platform that leaves it to the caller (Windows).  The
/// members are in the order the WebAuthn serialization algorithm fixes: type, challenge, origin,
/// crossOrigin.
/// </summary>
public static class WebAuthnClientData
{
    /// <summary>The <c>type</c> of a registration ceremony.</summary>
    public const string RegistrationType = "webauthn.create";

    /// <summary>The <c>type</c> of a sign-in ceremony.</summary>
    public const string AssertionType = "webauthn.get";

    /// <summary>The web origin of a relying party id: <c>https://</c> plus the id.</summary>
    /// <param name="relyingPartyId">A validated relying party id.</param>
    /// <returns>The origin.</returns>
    public static string OriginOf(string relyingPartyId)
    {
        ArgumentException.ThrowIfNullOrEmpty(relyingPartyId);
        return "https://" + relyingPartyId;
    }

    /// <summary>Writes client data.</summary>
    /// <param name="type"><see cref="RegistrationType"/> or <see cref="AssertionType"/>.</param>
    /// <param name="challenge">The relying party's challenge bytes.</param>
    /// <param name="origin">The origin to state.</param>
    /// <returns>The UTF-8 JSON the authenticator hashes and signs over.</returns>
    public static byte[] Write(string type, byte[] challenge, string origin)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentException.ThrowIfNullOrEmpty(origin);
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("challenge", Base64Url.EncodeToString(challenge));
            writer.WriteString("origin", origin);
            writer.WriteBoolean("crossOrigin", false);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
