using System.Text;
using System.Text.Json;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("WebAuthn JSON Should", Selector = "pkwj")]
public class WebAuthnJsonShould : UnitTestMenuContainer
{
    public WebAuthnJsonShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void TreatAnEmptyAlgorithmListAsTheDefaults()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("parses options whose pubKeyCredParams is empty", (unused, reg) =>
        {
            string empty = "{\"rp\":{\"id\":\"example.com\"},\"user\":{\"id\":\"AQID\",\"name\":\"ada\"},\"challenge\":\"BAUG\",\"pubKeyCredParams\":[]}";
            WebAuthnCreationRequest parsed = WebAuthnCreationRequest.Parse(new WebAuthnCreationOptions(empty), "example.com");
            return string.Join(",", parsed.Algorithms);
        })
        .TheTest
        .ShouldPass<string>((because, algorithms) =>
        {
            because.ItsTrue("an empty list means ES256 then RS256 (WebAuthn): " + algorithms, algorithms == "-7,-257");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ReadCreationOptions()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("parses full and minimal creation options", (unused, reg) =>
        {
            WebAuthnCreationRequest full = WebAuthnCreationRequest.Parse(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions()), "example.com");
            WebAuthnCreationRequest minimal = WebAuthnCreationRequest.Parse(new WebAuthnCreationOptions(
                "{\"rp\":{\"id\":\"example.com\"},\"user\":{\"id\":\"AQID\",\"name\":\"ada\"},\"challenge\":\"BAUG\",\"pubKeyCredParams\":[{\"type\":\"public-key\",\"alg\":-7}]}"), "example.com");
            return new CreationOutcome(
                full.RelyingPartyId,
                full.RelyingPartyName,
                Convert.ToHexString(full.Challenge),
                Convert.ToHexString(full.UserId),
                full.UserName,
                full.UserDisplayName,
                string.Join(",", full.Algorithms),
                full.TimeoutMilliseconds,
                string.Join(",", full.ExcludeCredentials.Select(c => Convert.ToHexString(c.Id) + ":" + string.Join("+", c.Transports))),
                full.AuthenticatorAttachment,
                full.ResidentKey,
                full.UserVerification,
                full.Attestation,
                minimal.RelyingPartyName + "|" + minimal.UserDisplayName + "|" + minimal.TimeoutMilliseconds + "|" + minimal.ExcludeCredentials.Count + "|" + minimal.AuthenticatorAttachment + "|" + minimal.ResidentKey + "|" + minimal.UserVerification + "|" + minimal.Attestation,
                full.ToString() + full.ExcludeCredentials[0].ToString());
        })
        .TheTest
        .ShouldPass<CreationOutcome>((because, outcome) =>
        {
            because.ItsTrue("the rp id is read", outcome.RpId == "example.com");
            because.ItsTrue("the rp name is read", outcome.RpName == "Example");
            because.ItsTrue("the challenge is decoded: " + outcome.Challenge, outcome.Challenge == "00010203FBFF");
            because.ItsTrue("the user id is decoded: " + outcome.UserId, outcome.UserId == "0A0B0C");
            because.ItsTrue("the user name is read", outcome.UserName == "ada@example.com");
            because.ItsTrue("the display name is read", outcome.DisplayName == "Ada Lovelace");
            because.ItsTrue("algorithms keep their order and unknown types are skipped: " + outcome.Algorithms, outcome.Algorithms == "-7,-257");
            because.ItsTrue("the timeout is read", outcome.Timeout == 120000);
            because.ItsTrue("excluded credentials are decoded and unknown types skipped: " + outcome.Excluded, outcome.Excluded == "DEAD:internal+hybrid,BEEF:");
            because.ItsTrue("the attachment is read", outcome.Attachment == "platform");
            because.ItsTrue("residentKey wins over requireResidentKey", outcome.ResidentKey == "preferred");
            because.ItsTrue("user verification is read", outcome.UserVerification == "required");
            because.ItsTrue("attestation is read", outcome.Attestation == "direct");
            because.ItsTrue("missing members take their defaults: " + outcome.MinimalDefaults, outcome.MinimalDefaults == "example.com|ada||0||discouraged|preferred|none");
            because.ItsTrue("printing a request names the type only: " + outcome.Printed, outcome.Printed == "WebAuthnCreationRequestWebAuthnCredentialDescriptor");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ReadRequestOptions()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("parses request options with and without an allow list", (unused, reg) =>
        {
            WebAuthnAssertionRequest named = WebAuthnAssertionRequest.Parse(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions()), "example.com");
            WebAuthnAssertionRequest discoverable = WebAuthnAssertionRequest.Parse(new WebAuthnRequestOptions("{\"rpId\":\"example.com\",\"challenge\":\"BAUG\"}"), "example.com");
            return new RequestOutcome(
                named.RelyingPartyId,
                Convert.ToHexString(named.Challenge),
                named.TimeoutMilliseconds,
                string.Join(",", named.AllowCredentials.Select(c => Convert.ToHexString(c.Id))),
                named.UserVerification,
                discoverable.AllowCredentials.Count,
                discoverable.UserVerification,
                discoverable.TimeoutMilliseconds is null,
                named.ToString());
        })
        .TheTest
        .ShouldPass<RequestOutcome>((because, outcome) =>
        {
            because.ItsTrue("the rp id is read", outcome.RpId == "example.com");
            because.ItsTrue("the challenge is decoded", outcome.Challenge == "00010203FBFF");
            because.ItsTrue("the timeout is read", outcome.Timeout == 60000);
            because.ItsTrue("the allow list is decoded: " + outcome.Allowed, outcome.Allowed == "DEAD,BEEF");
            because.ItsTrue("user verification is read", outcome.UserVerification == "discouraged");
            because.ItsTrue("no allow list means none", outcome.DiscoverableAllowCount == 0);
            because.ItsTrue("user verification defaults to preferred", outcome.DiscoverableUserVerification == "preferred");
            because.ItsTrue("no timeout means none", outcome.DiscoverableHasNoTimeout);
            because.ItsTrue("printing a request names the type only", outcome.Printed == "WebAuthnAssertionRequest");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseOptionsForAnotherRelyingParty()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("parses options whose relying party is absent or not the configured one", (unused, reg) =>
        {
            string creation = WebAuthnSamples.CreationOptions();
            string request = WebAuthnSamples.RequestOptions();
            List<string> accepted = new List<string>();
            string[] others = { "evil.example", "example.com.evil.example", "Example.com", "example.com.", "sub.example.com", " example.com", "example.co" };
            foreach (string other in others)
            {
                if (CreationCategory(creation.Replace("\"id\":\"example.com\"", "\"id\":\"" + other + "\"")) != PasskeyErrorCategory.InvalidOptions)
                {
                    accepted.Add("create:" + other);
                }
                if (RequestCategory(request.Replace("\"rpId\":\"example.com\"", "\"rpId\":\"" + other + "\"")) != PasskeyErrorCategory.InvalidOptions)
                {
                    accepted.Add("get:" + other);
                }
            }
            string message = CreationMessage(creation.Replace("\"id\":\"example.com\"", "\"id\":\"evil.example\""));
            return new RelyingPartyOutcome(
                string.Join(" ", accepted),
                CreationCategory(creation.Replace("\"id\":\"example.com\",", "")),
                RequestCategory(request.Replace("\"rpId\":\"example.com\",", "")),
                CreationCategory(creation.Replace("\"id\":\"example.com\"", "\"id\":42")),
                RequestCategory(request.Replace("\"rpId\":\"example.com\"", "\"rpId\":null")),
                // The app is configured for another relying party than the one the options name.
                Category(() => WebAuthnCreationRequest.Parse(new WebAuthnCreationOptions(creation), "other.example")),
                message.Contains("evil.example", StringComparison.Ordinal),
                message);
        })
        .TheTest
        .ShouldPass<RelyingPartyOutcome>((because, outcome) =>
        {
            because.ItsTrue("no other relying party is accepted: " + outcome.Accepted, outcome.Accepted.Length == 0);
            because.ItsTrue("creation options without rp.id are refused", outcome.CreationWithoutRpId == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("request options without rpId are refused", outcome.RequestWithoutRpId == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("a numeric rp.id is refused", outcome.NumericRpId == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("a null rpId is refused", outcome.NullRpId == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("options for example.com are refused by an app configured for another party", outcome.ConfiguredForAnother == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("the refusal does not quote the other relying party: " + outcome.Message, !outcome.MessageQuotesValue);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseMalformedOptions()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("parses each broken variant", (unused, reg) =>
        {
            string creation = WebAuthnSamples.CreationOptions();
            string request = WebAuthnSamples.RequestOptions();
            string secret = "SECRET-MARKER";
            Dictionary<string, string> creationVariants = new Dictionary<string, string>
            {
                ["not json"] = "{\"rp\": " + secret,
                ["an array"] = "[1,2]",
                ["no rp"] = creation.Replace("\"rp\":", "\"rq\":"),
                ["no user"] = creation.Replace("\"user\":", "\"usr\":"),
                ["no user name"] = creation.Replace("\"name\":\"ada@example.com\",", ""),
                ["no challenge"] = creation.Replace("\"challenge\":\"AAECA_v_\",", ""),
                ["padded challenge"] = creation.Replace("\"challenge\":\"AAECA_v_\"", "\"challenge\":\"AAECAw==\""),
                ["standard base64 challenge"] = creation.Replace("\"challenge\":\"AAECA_v_\"", "\"challenge\":\"AAECA+v/\""),
                ["challenge with whitespace"] = creation.Replace("\"challenge\":\"AAECA_v_\"", "\"challenge\":\"AAEC A_v_\""),
                ["empty challenge"] = creation.Replace("\"challenge\":\"AAECA_v_\"", "\"challenge\":\"\""),
                ["numeric challenge"] = creation.Replace("\"challenge\":\"AAECA_v_\"", "\"challenge\":7"),
                ["user id over 64 bytes"] = creation.Replace("\"id\":\"CgsM\"", "\"id\":\"" + new string('A', 88) + "\""),
                ["no algorithms"] = creation.Replace("\"pubKeyCredParams\":", "\"params\":"),
                ["no public-key algorithm"] = creation.Replace("\"type\":\"public-key\",\"alg\"", "\"type\":\"other\",\"alg\""),
                ["text algorithm"] = creation.Replace("\"alg\":-7", "\"alg\":\"ES256\""),
                ["zero timeout"] = creation.Replace("\"timeout\":120000", "\"timeout\":0"),
                ["text timeout"] = creation.Replace("\"timeout\":120000", "\"timeout\":\"soon\""),
                ["exclude list not an array"] = creation.Replace("\"excludeCredentials\":[", "\"excludeCredentials\":7,\"x\":["),
                ["exclude entry without id"] = creation.Replace("{\"type\":\"public-key\",\"id\":\"vu8\"}", "{\"type\":\"public-key\"}"),
                ["too many excluded"] = creation.Replace("{\"type\":\"public-key\",\"id\":\"vu8\"}", string.Join(",", Enumerable.Repeat("{\"type\":\"public-key\",\"id\":\"vu8\"}", 70))),
                ["text transports"] = creation.Replace("[\"internal\",\"hybrid\"]", "\"internal\""),
                // Duplicate members: the parser would keep the last, a provider handed the raw JSON might keep the first.
                ["duplicate rp"] = creation.Replace("\"rp\":{", "\"rp\":{\"id\":\"evil.example\"},\"rp\":{"),
                ["duplicate rp.id"] = creation.Replace("\"rp\":{", "\"rp\":{\"id\":\"evil.example\","),
                ["duplicate challenge"] = creation.Replace("\"challenge\":\"AAECA_v_\",", "\"challenge\":\"AAECA_v_\",\"challenge\":\"AAECA_v_\","),
            };
            List<string> accepted = new List<string>();
            List<string> leaked = new List<string>();
            foreach (KeyValuePair<string, string> variant in creationVariants)
            {
                if (CreationCategory(variant.Value) != PasskeyErrorCategory.InvalidOptions)
                {
                    accepted.Add(variant.Key);
                }
                string message = CreationMessage(variant.Value);
                if (message.Contains(secret, StringComparison.Ordinal) || message.Contains("AAEC", StringComparison.Ordinal) || message.Contains("evil", StringComparison.Ordinal))
                {
                    leaked.Add(variant.Key);
                }
            }
            if (RequestCategory("{\"rpId\":\"example.com\"}") != PasskeyErrorCategory.InvalidOptions)
            {
                accepted.Add("request without challenge");
            }
            if (RequestCategory(request.Replace("\"allowCredentials\":[", "\"allowCredentials\":7,\"x\":[")) != PasskeyErrorCategory.InvalidOptions)
            {
                accepted.Add("request allow list not an array");
            }
            if (RequestCategory(request.Replace("\"rpId\":\"example.com\"", "\"rpId\":\"evil.example\",\"rpId\":\"example.com\"")) != PasskeyErrorCategory.InvalidOptions)
            {
                accepted.Add("request with a duplicate rpId");
            }
            return new MalformedOutcome(creationVariants.Count, string.Join(", ", accepted), string.Join(", ", leaked));
        })
        .TheTest
        .ShouldPass<MalformedOutcome>((because, outcome) =>
        {
            because.ItsTrue("the variants ran", outcome.Variants > 15);
            because.ItsTrue("every broken variant is invalid options; accepted: " + outcome.Accepted, outcome.Accepted.Length == 0);
            because.ItsTrue("no refusal quotes the input; leaked: " + outcome.Leaked, outcome.Leaked.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void WriteResponseJson()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("writes a registration response, an assertion response and client data", (unused, reg) =>
        {
            byte[] id = { 0xFB, 0xFF, 0x01 };
            byte[] clientData = WebAuthnClientData.Write(WebAuthnClientData.RegistrationType, new byte[] { 0, 1, 2, 3, 0xFB, 0xFF }, WebAuthnClientData.OriginOf("example.com"));
            WebAuthnRegistrationResponse registration = WebAuthnResponseJson.Registration(id, clientData, new byte[] { 9, 8, 7 }, new string[] { "internal", "hybrid" }, WebAuthnResponseJson.PlatformAttachment);
            WebAuthnAssertionResponse assertion = WebAuthnResponseJson.Assertion(id, clientData, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }, null);
            WebAuthnAssertionResponse withoutUser = WebAuthnResponseJson.Assertion(id, clientData, new byte[] { 1 }, new byte[] { 2 }, Array.Empty<byte>(), WebAuthnResponseJson.CrossPlatformAttachment);

            using JsonDocument registered = JsonDocument.Parse(registration.Json);
            using JsonDocument asserted = JsonDocument.Parse(assertion.Json);
            using JsonDocument assertedWithoutUser = JsonDocument.Parse(withoutUser.Json);
            JsonElement r = registered.RootElement;
            JsonElement a = asserted.RootElement;
            return new ResponseOutcome(
                Encoding.UTF8.GetString(clientData),
                r.GetProperty("id").GetString() + "|" + r.GetProperty("rawId").GetString() + "|" + r.GetProperty("type").GetString() + "|" + r.GetProperty("authenticatorAttachment").GetString(),
                r.GetProperty("response").GetProperty("attestationObject").GetString() + "|" + string.Join("+", r.GetProperty("response").GetProperty("transports").EnumerateArray().Select(t => t.GetString())),
                r.GetProperty("response").GetProperty("clientDataJSON").GetString() == System.Buffers.Text.Base64Url.EncodeToString(clientData),
                r.GetProperty("clientExtensionResults").ValueKind == JsonValueKind.Object,
                a.GetProperty("response").GetProperty("authenticatorData").GetString() + "|" + a.GetProperty("response").GetProperty("signature").GetString() + "|" + a.GetProperty("response").GetProperty("userHandle").GetString(),
                a.TryGetProperty("authenticatorAttachment", out JsonElement _),
                assertedWithoutUser.RootElement.GetProperty("response").TryGetProperty("userHandle", out JsonElement _),
                assertedWithoutUser.RootElement.GetProperty("authenticatorAttachment").GetString(),
                Category(() => WebAuthnResponseJson.Registration(null, clientData, new byte[] { 1 }, Array.Empty<string>(), null)),
                Category(() => WebAuthnResponseJson.Registration(id, clientData, Array.Empty<byte>(), Array.Empty<string>(), null)),
                Category(() => WebAuthnResponseJson.Assertion(id, clientData, new byte[] { 1 }, null, null, null)));
        })
        .TheTest
        .ShouldPass<ResponseOutcome>((because, outcome) =>
        {
            because.ItsTrue("client data is exactly type, challenge, origin, crossOrigin: " + outcome.ClientData,
                outcome.ClientData == "{\"type\":\"webauthn.create\",\"challenge\":\"AAECA_v_\",\"origin\":\"https://example.com\",\"crossOrigin\":false}");
            because.ItsTrue("the registration head is unpadded base64url: " + outcome.RegistrationHead, outcome.RegistrationHead == "-_8B|-_8B|public-key|platform");
            because.ItsTrue("the attestation object and transports are written: " + outcome.RegistrationBody, outcome.RegistrationBody == "CQgH|internal+hybrid");
            because.ItsTrue("the client data is carried unchanged", outcome.ClientDataCarried);
            because.ItsTrue("extension results are an empty object", outcome.HasExtensionResults);
            because.ItsTrue("the assertion body is written: " + outcome.AssertionBody, outcome.AssertionBody == "AQ|Ag|Aw");
            because.ItsTrue("no attachment is claimed when the platform did not say", !outcome.AssertionClaimsAttachment);
            because.ItsTrue("an empty user handle is left out", !outcome.EmptyUserHandleWritten);
            because.ItsTrue("a stated attachment is written", outcome.StatedAttachment == "cross-platform");
            because.ItsTrue("a missing credential id is a platform failure", outcome.MissingId == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("an empty attestation object is a platform failure", outcome.EmptyAttestation == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("a missing signature is a platform failure", outcome.MissingSignature == PasskeyErrorCategory.PlatformFailure);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static PasskeyErrorCategory? CreationCategory(string json)
    {
        return Category(() => WebAuthnCreationRequest.Parse(new WebAuthnCreationOptions(json), "example.com"));
    }

    private static PasskeyErrorCategory? RequestCategory(string json)
    {
        return Category(() => WebAuthnAssertionRequest.Parse(new WebAuthnRequestOptions(json), "example.com"));
    }

    private static string CreationMessage(string json)
    {
        try
        {
            WebAuthnCreationRequest.Parse(new WebAuthnCreationOptions(json), "example.com");
            return string.Empty;
        }
        catch (PasskeyException exception)
        {
            return exception.ToString();
        }
    }

    private static PasskeyErrorCategory? Category(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (PasskeyException exception)
        {
            return exception.Category;
        }
    }

    private sealed record CreationOutcome(string RpId, string RpName, string Challenge, string UserId, string UserName, string DisplayName, string Algorithms, int? Timeout, string Excluded, string? Attachment, string ResidentKey, string UserVerification, string Attestation, string MinimalDefaults, string Printed);

    private sealed record RequestOutcome(string RpId, string Challenge, int? Timeout, string Allowed, string UserVerification, int DiscoverableAllowCount, string DiscoverableUserVerification, bool DiscoverableHasNoTimeout, string Printed);

    private sealed record RelyingPartyOutcome(string Accepted, PasskeyErrorCategory? CreationWithoutRpId, PasskeyErrorCategory? RequestWithoutRpId, PasskeyErrorCategory? NumericRpId, PasskeyErrorCategory? NullRpId, PasskeyErrorCategory? ConfiguredForAnother, bool MessageQuotesValue, string Message);

    private sealed record MalformedOutcome(int Variants, string Accepted, string Leaked);

    private sealed record ResponseOutcome(string ClientData, string RegistrationHead, string RegistrationBody, bool ClientDataCarried, bool HasExtensionResults, string AssertionBody, bool AssertionClaimsAttachment, bool EmptyUserHandleWritten, string? StatedAttachment, PasskeyErrorCategory? MissingId, PasskeyErrorCategory? EmptyAttestation, PasskeyErrorCategory? MissingSignature);
}
