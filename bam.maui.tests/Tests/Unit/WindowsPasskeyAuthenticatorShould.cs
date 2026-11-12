using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

// The authenticator is exercised against a scripted webauthn.dll: a real ceremony needs a person at the Windows Hello prompt.
#pragma warning disable CA1416
[UnitTestMenu("WindowsPasskeyAuthenticator Should", Selector = "pkwn")]
public class WindowsPasskeyAuthenticatorShould : UnitTestMenuContainer
{
    private static readonly IntPtr Window = new IntPtr(0x1234);

    public WindowsPasskeyAuthenticatorShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void CreateAPasskey()
    {
        After.Setup(reg => reg.Set(new FakeWindowsWebAuthnApi()))
        .When<FakeWindowsWebAuthnApi>("creates a passkey from the sample options", (api, reg) =>
        {
            WindowsPasskeyAuthenticator authenticator = Authenticator(api);
            WebAuthnRegistrationResponse response = Wait(authenticator.CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions())));
            WindowsMakeCredentialCall call = api.MakeCredentialCall!;
            using JsonDocument document = JsonDocument.Parse(response.Json);
            JsonElement root = document.RootElement;

            FakeWindowsWebAuthnApi required = new FakeWindowsWebAuthnApi();
            Wait(Authenticator(required).CreateAsync(new WebAuthnCreationOptions(
                "{\"rp\":{\"id\":\"example.com\"},\"user\":{\"id\":\"AQID\",\"name\":\"ada\"},\"challenge\":\"BAUG\",\"pubKeyCredParams\":[{\"type\":\"public-key\",\"alg\":-7}],\"authenticatorSelection\":{\"requireResidentKey\":true},\"attestation\":\"enterprise\"}")));

            return new CreateOutcome(
                api.Window == Window,
                call.RelyingPartyId + "|" + call.RelyingPartyName + "|" + call.UserName + "|" + call.UserDisplayName + "|" + Convert.ToHexString(call.UserId),
                string.Join(",", call.Algorithms),
                Encoding.UTF8.GetString(call.ClientDataJson),
                call.TimeoutMilliseconds,
                string.Join(",", call.ExcludeCredentialIds.Select(Convert.ToHexString)),
                call.AuthenticatorAttachment + "|" + call.RequireResidentKey + "|" + call.PreferResidentKey + "|" + call.UserVerificationRequirement + "|" + call.AttestationConveyancePreference,
                root.GetProperty("id").GetString() + "|" + root.GetProperty("authenticatorAttachment").GetString() + "|" + root.GetProperty("response").GetProperty("attestationObject").GetString() + "|" + string.Join("+", root.GetProperty("response").GetProperty("transports").EnumerateArray().Select(t => t.GetString())),
                root.GetProperty("response").GetProperty("clientDataJSON").GetString() == System.Buffers.Text.Base64Url.EncodeToString(call.ClientDataJson),
                required.MakeCredentialCall!.RequireResidentKey + "|" + required.MakeCredentialCall.PreferResidentKey + "|" + required.MakeCredentialCall.TimeoutMilliseconds + "|" + required.MakeCredentialCall.AuthenticatorAttachment + "|" + required.MakeCredentialCall.AttestationConveyancePreference);
        })
        .TheTest
        .ShouldPass<CreateOutcome>((because, outcome) =>
        {
            because.ItsTrue("the prompt is given the app's window", outcome.WindowPassed);
            because.ItsTrue("the relying party and user are passed: " + outcome.Entities, outcome.Entities == "example.com|Example|ada@example.com|Ada Lovelace|0A0B0C");
            because.ItsTrue("the algorithms are passed in order: " + outcome.Algorithms, outcome.Algorithms == "-7,-257");
            because.ItsTrue("the client data names the create ceremony, the challenge and the relying party's origin: " + outcome.ClientData,
                outcome.ClientData == "{\"type\":\"webauthn.create\",\"challenge\":\"AAECA_v_\",\"origin\":\"https://example.com\",\"crossOrigin\":false}");
            because.ItsTrue("the timeout is passed", outcome.Timeout == 120000);
            because.ItsTrue("the excluded credentials are passed: " + outcome.Excluded, outcome.Excluded == "DEAD,BEEF");
            because.ItsTrue("platform attachment, preferred resident key, required verification, direct attestation: " + outcome.Selection, outcome.Selection == "1|False|True|1|3");
            because.ItsTrue("the response carries the id, attachment, attestation object and transport: " + outcome.Response, outcome.Response == "3q0|platform|CQgH|internal");
            because.ItsTrue("the response carries the client data that was signed", outcome.ClientDataCarried);
            because.ItsTrue("requireResidentKey alone is required, enterprise attestation asks for none, defaults fill the rest: " + outcome.RequiredDefaults, outcome.RequiredDefaults == "True|False|60000|0|1");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SignInWithAPasskey()
    {
        After.Setup(reg => reg.Set(new FakeWindowsWebAuthnApi()))
        .When<FakeWindowsWebAuthnApi>("signs in from the sample options", (api, reg) =>
        {
            WebAuthnAssertionResponse response = Wait(Authenticator(api).AssertAsync(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions())));
            WindowsGetAssertionCall call = api.GetAssertionCall!;
            using JsonDocument document = JsonDocument.Parse(response.Json);
            JsonElement body = document.RootElement.GetProperty("response");
            return new AssertOutcome(
                call.RelyingPartyId,
                Encoding.UTF8.GetString(call.ClientDataJson),
                call.TimeoutMilliseconds,
                string.Join(",", call.AllowCredentialIds.Select(Convert.ToHexString)),
                call.UserVerificationRequirement,
                document.RootElement.GetProperty("id").GetString() + "|" + body.GetProperty("authenticatorData").GetString() + "|" + body.GetProperty("signature").GetString() + "|" + body.GetProperty("userHandle").GetString(),
                document.RootElement.TryGetProperty("authenticatorAttachment", out JsonElement _));
        })
        .TheTest
        .ShouldPass<AssertOutcome>((because, outcome) =>
        {
            because.ItsTrue("the relying party is passed", outcome.RpId == "example.com");
            because.ItsTrue("the client data names the get ceremony: " + outcome.ClientData,
                outcome.ClientData == "{\"type\":\"webauthn.get\",\"challenge\":\"AAECA_v_\",\"origin\":\"https://example.com\",\"crossOrigin\":false}");
            because.ItsTrue("the timeout is passed", outcome.Timeout == 60000);
            because.ItsTrue("the allow list is passed: " + outcome.Allowed, outcome.Allowed == "DEAD,BEEF");
            because.ItsTrue("discouraged verification is passed", outcome.UserVerification == 3);
            because.ItsTrue("the response carries id, authenticator data, signature and user handle: " + outcome.Response, outcome.Response == "3q0|AQ|Ag|Cg");
            because.ItsTrue("no attachment is claimed", !outcome.ClaimsAttachment);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NeverCallWindowsForAnotherRelyingParty()
    {
        After.Setup(reg => reg.Set(new FakeWindowsWebAuthnApi()))
        .When<FakeWindowsWebAuthnApi>("is handed options for another relying party, malformed options and no window", (api, reg) =>
        {
            WindowsPasskeyAuthenticator authenticator = Authenticator(api);
            PasskeyErrorCategory? otherCreate = Category(() => Wait(authenticator.CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions().Replace("\"id\":\"example.com\"", "\"id\":\"evil.example\"")))));
            PasskeyErrorCategory? otherGet = Category(() => Wait(authenticator.AssertAsync(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions().Replace("\"rpId\":\"example.com\"", "\"rpId\":\"evil.example\"")))));
            PasskeyErrorCategory? malformed = Category(() => Wait(authenticator.AssertAsync(new WebAuthnRequestOptions("{"))));
            int callsAfterRefusals = api.Calls;

            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool preCancelledThrows = Cancels(() => Wait(authenticator.AssertAsync(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions()), cancelled.Token)));
            int callsAfterCancel = api.Calls;

            WindowsPasskeyAuthenticator windowless = new WindowsPasskeyAuthenticator(PasskeyClientFixture.ValidConfiguration(), () => IntPtr.Zero, api);
            PasskeyErrorCategory? noWindow = Category(() => Wait(windowless.AssertAsync(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions()))));

            return new RefusalOutcome(otherCreate, otherGet, malformed, callsAfterRefusals, preCancelledThrows, callsAfterCancel, noWindow, api.Calls);
        })
        .TheTest
        .ShouldPass<RefusalOutcome>((because, outcome) =>
        {
            because.ItsTrue("creation options for another relying party are invalid options", outcome.OtherCreate == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("request options for another relying party are invalid options", outcome.OtherGet == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("malformed options are invalid options", outcome.Malformed == PasskeyErrorCategory.InvalidOptions);
            because.ItsTrue("Windows was not called for any of them", outcome.CallsAfterRefusals == 0);
            because.ItsTrue("a token cancelled beforehand throws cancellation", outcome.PreCancelledThrows);
            because.ItsTrue("and Windows is not called", outcome.CallsAfterCancel == 0);
            because.ItsTrue("no window is a platform failure", outcome.NoWindow == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("and Windows is still not called", outcome.CallsAtEnd == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ReportWhatWindowsReturned()
    {
        After.Setup(reg => reg.Set(new FakeWindowsWebAuthnApi()))
        .When<FakeWindowsWebAuthnApi>("returns each result code from a ceremony", (unused, reg) =>
        {
            Dictionary<int, PasskeyErrorCategory> creating = new Dictionary<int, PasskeyErrorCategory>
            {
                [WindowsPasskeyErrors.UserCancelled] = PasskeyErrorCategory.UserCancelled,
                [WindowsPasskeyErrors.Win32Cancelled] = PasskeyErrorCategory.UserCancelled,
                [WindowsPasskeyErrors.Win32Timeout] = PasskeyErrorCategory.ExpiredChallenge,
                [WindowsPasskeyErrors.Exists] = PasskeyErrorCategory.CredentialAlreadyRegistered,
                [WindowsPasskeyErrors.NotFound] = PasskeyErrorCategory.PlatformFailure,
                [WindowsPasskeyErrors.DeviceNotFound] = PasskeyErrorCategory.PlatformFailure,
                [WindowsPasskeyErrors.InvalidParameter] = PasskeyErrorCategory.InvalidOptions,
                [WindowsPasskeyErrors.NotSupported] = PasskeyErrorCategory.PlatformFailure,
                [WindowsPasskeyErrors.Win32NotSupported] = PasskeyErrorCategory.PlatformFailure,
                [WindowsPasskeyErrors.StorageFull] = PasskeyErrorCategory.PlatformFailure,
                [unchecked((int)0x80004005)] = PasskeyErrorCategory.PlatformFailure
            };
            Dictionary<int, PasskeyErrorCategory> signingIn = new Dictionary<int, PasskeyErrorCategory>(creating)
            {
                [WindowsPasskeyErrors.Exists] = PasskeyErrorCategory.PlatformFailure,
                [WindowsPasskeyErrors.NotFound] = PasskeyErrorCategory.NoCredentials,
                [WindowsPasskeyErrors.DeviceNotFound] = PasskeyErrorCategory.NoCredentials
            };
            List<string> wrong = new List<string>();
            foreach (KeyValuePair<int, PasskeyErrorCategory> expected in creating)
            {
                FakeWindowsWebAuthnApi api = new FakeWindowsWebAuthnApi { Attestation = new WindowsAttestation { Result = expected.Key } };
                PasskeyErrorCategory? actual = Category(() => Wait(Authenticator(api).CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions()))));
                if (actual != expected.Value)
                {
                    wrong.Add("create 0x" + expected.Key.ToString("X8") + "=" + actual);
                }
            }
            foreach (KeyValuePair<int, PasskeyErrorCategory> expected in signingIn)
            {
                FakeWindowsWebAuthnApi api = new FakeWindowsWebAuthnApi { Assertion = new WindowsAssertion { Result = expected.Key } };
                PasskeyErrorCategory? actual = Category(() => Wait(Authenticator(api).AssertAsync(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions()))));
                if (actual != expected.Value)
                {
                    wrong.Add("get 0x" + expected.Key.ToString("X8") + "=" + actual);
                }
            }

            FakeWindowsWebAuthnApi emptySuccess = new FakeWindowsWebAuthnApi { Attestation = new WindowsAttestation { Result = WindowsPasskeyErrors.Success } };
            FakeWindowsWebAuthnApi missingLibrary = new FakeWindowsWebAuthnApi { CallFailure = new DllNotFoundException("webauthn.dll") };
            FakeWindowsWebAuthnApi unknownTransport = new FakeWindowsWebAuthnApi { Attestation = new WindowsAttestation { Result = WindowsPasskeyErrors.Success, AttestationObject = new byte[] { 1 }, CredentialId = new byte[] { 2 }, UsedTransport = 0 } };
            using JsonDocument withoutTransport = JsonDocument.Parse(Wait(Authenticator(unknownTransport).CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions()))).Json);
            FakeWindowsWebAuthnApi usb = new FakeWindowsWebAuthnApi { Attestation = new WindowsAttestation { Result = WindowsPasskeyErrors.Success, AttestationObject = new byte[] { 1 }, CredentialId = new byte[] { 2 }, UsedTransport = 0x01 } };
            using JsonDocument withUsb = JsonDocument.Parse(Wait(Authenticator(usb).CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions()))).Json);

            // The caller cancels while the prompt is up; the token reaches the library call.
            FakeWindowsWebAuthnApi cancelledDuring = new FakeWindowsWebAuthnApi { Assertion = new WindowsAssertion { Result = WindowsPasskeyErrors.Win32Cancelled } };
            using CancellationTokenSource source = new CancellationTokenSource();
            cancelledDuring.DuringCall = source.Cancel;
            PasskeyErrorCategory? cancelled = Category(() => Wait(Authenticator(cancelledDuring).AssertAsync(new WebAuthnRequestOptions(WebAuthnSamples.RequestOptions()), source.Token)));

            return new ResultOutcome(
                string.Join(", ", wrong),
                Category(() => Wait(Authenticator(emptySuccess).CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions())))),
                Category(() => Wait(Authenticator(missingLibrary).CreateAsync(new WebAuthnCreationOptions(WebAuthnSamples.CreationOptions())))),
                withoutTransport.RootElement.TryGetProperty("authenticatorAttachment", out JsonElement _),
                withoutTransport.RootElement.GetProperty("response").GetProperty("transports").GetArrayLength(),
                withUsb.RootElement.GetProperty("authenticatorAttachment").GetString() + "|" + string.Join("+", withUsb.RootElement.GetProperty("response").GetProperty("transports").EnumerateArray().Select(t => t.GetString())),
                cancelled,
                cancelledDuring.TokenWasCancelledDuringCall);
        })
        .TheTest
        .ShouldPass<ResultOutcome>((because, outcome) =>
        {
            because.ItsTrue("every result code lands in its category; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("success without a credential is a platform failure", outcome.EmptySuccess == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("a missing webauthn.dll is an unsupported operating system", outcome.MissingLibrary == PasskeyErrorCategory.UnsupportedOperatingSystem);
            because.ItsTrue("no attachment is claimed when the transport is unknown", !outcome.UnknownTransportClaimsAttachment);
            because.ItsTrue("and no transports are listed", outcome.UnknownTransportCount == 0);
            because.ItsTrue("a security key is cross-platform over usb: " + outcome.Usb, outcome.Usb == "cross-platform|usb");
            because.ItsTrue("a cancelled prompt is user cancelled", outcome.Cancelled == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("the caller's token reaches the library call", outcome.TokenReachedCall);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void ReportCapabilities()
    {
        After.Setup(reg => reg.Set(new FakeWindowsWebAuthnApi()))
        .When<FakeWindowsWebAuthnApi>("probes with and without Windows Hello and without the library", (api, reg) =>
        {
            PasskeyCapabilities withHello = Wait(Authenticator(api).GetCapabilitiesAsync());
            PasskeyCapabilities withoutHello = Wait(Authenticator(new FakeWindowsWebAuthnApi { HelloAvailable = false }).GetCapabilitiesAsync());
            PasskeyCapabilities noLibrary = Wait(Authenticator(new FakeWindowsWebAuthnApi { VersionFailure = new DllNotFoundException() }).GetCapabilitiesAsync());
            PasskeyCapabilities noEntryPoint = Wait(Authenticator(new FakeWindowsWebAuthnApi { VersionFailure = new EntryPointNotFoundException() }).GetCapabilitiesAsync());
            PasskeyCapabilities versionZero = Wait(Authenticator(new FakeWindowsWebAuthnApi { ApiVersion = 0 }).GetCapabilitiesAsync());
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            return new CapabilityOutcome(
                withHello.Availability + "|" + withHello.SupportsDiscoverableCredentials + "|" + withHello.SupportsSecurityKeys + "|" + withHello.Platform,
                withoutHello.Availability + "|" + withoutHello.SupportsDiscoverableCredentials + "|" + withoutHello.SupportsSecurityKeys,
                noLibrary.Availability,
                noEntryPoint.Availability,
                versionZero.Availability,
                Cancels(() => Wait(Authenticator(api).GetCapabilitiesAsync(cancelled.Token))),
                api.Calls);
        })
        .TheTest
        .ShouldPass<CapabilityOutcome>((because, outcome) =>
        {
            because.ItsTrue("with Windows Hello everything is available: " + outcome.WithHello, outcome.WithHello == "Available|True|True|Windows");
            because.ItsTrue("without Windows Hello a security key still works: " + outcome.WithoutHello, outcome.WithoutHello == "Available|False|True");
            because.ItsTrue("no library is an unsupported operating system", outcome.NoLibrary == PasskeyAvailability.UnsupportedOperatingSystem);
            because.ItsTrue("no entry point is an unsupported operating system", outcome.NoEntryPoint == PasskeyAvailability.UnsupportedOperatingSystem);
            because.ItsTrue("API version 0 is an unsupported operating system", outcome.VersionZero == PasskeyAvailability.UnsupportedOperatingSystem);
            because.ItsTrue("a cancelled probe throws cancellation", outcome.CancelledProbeThrows);
            because.ItsTrue("a probe never starts a ceremony", outcome.CeremonyCalls == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void MatchTheLayoutOfWebAuthnDotH()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("measures the interop structures and asks the real library its version", (unused, reg) =>
        {
            // Sizes and offsets printed by a C program compiled against Microsoft's webauthn.h for x64.
            List<string> wrong = new List<string>();
            if (IntPtr.Size == 8)
            {
                Check(wrong, "RP_ENTITY_INFORMATION", Marshal.SizeOf<WindowsWebAuthnApi.RpEntityInformation>(), 32);
                Check(wrong, "USER_ENTITY_INFORMATION", Marshal.SizeOf<WindowsWebAuthnApi.UserEntityInformation>(), 40);
                Check(wrong, "CLIENT_DATA", Marshal.SizeOf<WindowsWebAuthnApi.ClientData>(), 24);
                Check(wrong, "COSE_CREDENTIAL_PARAMETER", Marshal.SizeOf<WindowsWebAuthnApi.CoseCredentialParameter>(), 24);
                Check(wrong, "COSE_CREDENTIAL_PARAMETERS", Marshal.SizeOf<WindowsWebAuthnApi.CoseCredentialParameters>(), 16);
                Check(wrong, "CREDENTIAL", Marshal.SizeOf<WindowsWebAuthnApi.Credential>(), 24);
                Check(wrong, "CREDENTIALS", Marshal.SizeOf<WindowsWebAuthnApi.Credentials>(), 16);
                Check(wrong, "EXTENSIONS", Marshal.SizeOf<WindowsWebAuthnApi.Extensions>(), 16);
                Check(wrong, "MAKE_CREDENTIAL_OPTIONS.pCancellationId", Offset<WindowsWebAuthnApi.MakeCredentialOptions>("pCancellationId"), 64);
                Check(wrong, "MAKE_CREDENTIAL_OPTIONS.bPreferResidentKey", Offset<WindowsWebAuthnApi.MakeCredentialOptions>("bPreferResidentKey"), 88);
                Check(wrong, "GET_ASSERTION_OPTIONS.pCancellationId", Offset<WindowsWebAuthnApi.GetAssertionOptions>("pCancellationId"), 72);
                Check(wrong, "GET_ASSERTION_OPTIONS.pAllowCredentialList", Offset<WindowsWebAuthnApi.GetAssertionOptions>("pAllowCredentialList"), 80);
                Check(wrong, "CREDENTIAL_ATTESTATION.pbAttestationObject", Offset<WindowsWebAuthnApi.CredentialAttestation>("pbAttestationObject"), 72);
                Check(wrong, "CREDENTIAL_ATTESTATION.pbCredentialId", Offset<WindowsWebAuthnApi.CredentialAttestation>("pbCredentialId"), 88);
                Check(wrong, "CREDENTIAL_ATTESTATION.Extensions", Offset<WindowsWebAuthnApi.CredentialAttestationWithTransport>("Extensions"), 96);
                Check(wrong, "CREDENTIAL_ATTESTATION.dwUsedTransport", Offset<WindowsWebAuthnApi.CredentialAttestationWithTransport>("dwUsedTransport"), 112);
                Check(wrong, "ASSERTION.Credential", Offset<WindowsWebAuthnApi.Assertion>("Credential"), 32);
                Check(wrong, "ASSERTION.pbUserId", Offset<WindowsWebAuthnApi.Assertion>("pbUserId"), 64);
            }

            // Read-only calls into the real library: neither shows a prompt.
            int version = -1;
            bool probed = false;
            if (OperatingSystem.IsWindows())
            {
                WindowsWebAuthnApi real = new WindowsWebAuthnApi();
                try
                {
                    version = real.GetApiVersionNumber();
                    real.IsUserVerifyingPlatformAuthenticatorAvailable();
                    probed = true;
                }
                catch (DllNotFoundException)
                {
                    version = 0;
                    probed = true;
                }
            }
            return new LayoutOutcome(string.Join(", ", wrong), OperatingSystem.IsWindows(), probed, version);
        })
        .TheTest
        .ShouldPass<LayoutOutcome>((because, outcome) =>
        {
            because.ItsTrue("every structure matches the header; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("on Windows the real library answers the probes, version " + outcome.Version, !outcome.OnWindows || (outcome.Probed && outcome.Version >= 0));
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private static void Check(List<string> wrong, string name, int actual, int expected)
    {
        if (actual != expected)
        {
            wrong.Add(name + " is " + actual + ", header says " + expected);
        }
    }

    private static int Offset<TStructure>(string field)
    {
        return (int)Marshal.OffsetOf<TStructure>(field);
    }

    private static WindowsPasskeyAuthenticator Authenticator(FakeWindowsWebAuthnApi api)
    {
        return new WindowsPasskeyAuthenticator(PasskeyClientFixture.ValidConfiguration(), () => Window, api);
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

    private static bool Cancels(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static TResult Wait<TResult>(Task<TResult> task)
    {
        return task.GetAwaiter().GetResult();
    }

    private sealed record CreateOutcome(bool WindowPassed, string Entities, string Algorithms, string ClientData, int Timeout, string Excluded, string Selection, string Response, bool ClientDataCarried, string RequiredDefaults);

    private sealed record AssertOutcome(string RpId, string ClientData, int Timeout, string Allowed, int UserVerification, string Response, bool ClaimsAttachment);

    private sealed record RefusalOutcome(PasskeyErrorCategory? OtherCreate, PasskeyErrorCategory? OtherGet, PasskeyErrorCategory? Malformed, int CallsAfterRefusals, bool PreCancelledThrows, int CallsAfterCancel, PasskeyErrorCategory? NoWindow, int CallsAtEnd);

    private sealed record ResultOutcome(string Wrong, PasskeyErrorCategory? EmptySuccess, PasskeyErrorCategory? MissingLibrary, bool UnknownTransportClaimsAttachment, int UnknownTransportCount, string Usb, PasskeyErrorCategory? Cancelled, bool TokenReachedCall);

    private sealed record CapabilityOutcome(string WithHello, string WithoutHello, PasskeyAvailability NoLibrary, PasskeyAvailability NoEntryPoint, PasskeyAvailability VersionZero, bool CancelledProbeThrows, int CeremonyCalls);

    private sealed record LayoutOutcome(string Wrong, bool OnWindows, bool Probed, int Version);
}
#pragma warning restore CA1416
