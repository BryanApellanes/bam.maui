using Bam.Maui.Passkeys;

namespace Bam.Maui.Tests.TestClasses;

/// <summary>WebAuthn options JSON for example.com, as a relying party would issue it.</summary>
public static class WebAuthnSamples
{
    // challenge AAECA_v_ = 00 01 02 03 FB FF; user id CgsM = 0A 0B 0C; credential ids 3q0 = DE AD, vu8 = BE EF.
    public static string CreationOptions()
    {
        return "{\"rp\":{\"id\":\"example.com\",\"name\":\"Example\"},"
            + "\"user\":{\"id\":\"CgsM\",\"name\":\"ada@example.com\",\"displayName\":\"Ada Lovelace\"},"
            + "\"challenge\":\"AAECA_v_\","
            + "\"pubKeyCredParams\":[{\"type\":\"public-key\",\"alg\":-7},{\"type\":\"future-key\",\"alg\":-8},{\"type\":\"public-key\",\"alg\":-257}],"
            + "\"timeout\":120000,"
            + "\"excludeCredentials\":[{\"type\":\"public-key\",\"id\":\"3q0\",\"transports\":[\"internal\",\"hybrid\"]},{\"type\":\"future-key\",\"id\":\"AAAA\"},{\"type\":\"public-key\",\"id\":\"vu8\"}],"
            + "\"authenticatorSelection\":{\"authenticatorAttachment\":\"platform\",\"residentKey\":\"preferred\",\"requireResidentKey\":true,\"userVerification\":\"required\"},"
            + "\"attestation\":\"direct\"}";
    }

    public static string RequestOptions()
    {
        return "{\"rpId\":\"example.com\","
            + "\"challenge\":\"AAECA_v_\","
            + "\"timeout\":60000,"
            + "\"allowCredentials\":[{\"type\":\"public-key\",\"id\":\"3q0\"},{\"type\":\"public-key\",\"id\":\"vu8\"}],"
            + "\"userVerification\":\"discouraged\"}";
    }
}

/// <summary>A scripted stand-in for <c>webauthn.dll</c> that records what it was asked.</summary>
internal sealed class FakeWindowsWebAuthnApi : IWindowsWebAuthnApi
{
    public int ApiVersion { get; set; } = 9;
    public Exception? VersionFailure { get; set; }
    public bool HelloAvailable { get; set; } = true;
    public WindowsAttestation Attestation { get; set; } = new WindowsAttestation { Result = WindowsPasskeyErrors.Success, AttestationObject = new byte[] { 9, 8, 7 }, CredentialId = new byte[] { 0xDE, 0xAD }, UsedTransport = 0x10 };
    public WindowsAssertion Assertion { get; set; } = new WindowsAssertion { Result = WindowsPasskeyErrors.Success, AuthenticatorData = new byte[] { 1 }, Signature = new byte[] { 2 }, CredentialId = new byte[] { 0xDE, 0xAD }, UserId = new byte[] { 0x0A } };
    public Exception? CallFailure { get; set; }
    public Action? DuringCall { get; set; }

    public WindowsMakeCredentialCall? MakeCredentialCall { get; private set; }
    public WindowsGetAssertionCall? GetAssertionCall { get; private set; }
    public IntPtr Window { get; private set; }
    public bool TokenWasCancelledDuringCall { get; private set; }
    public int Calls { get; private set; }

    public int GetApiVersionNumber()
    {
        if (VersionFailure is not null)
        {
            throw VersionFailure;
        }
        return ApiVersion;
    }

    public bool IsUserVerifyingPlatformAuthenticatorAvailable()
    {
        return HelloAvailable;
    }

    public WindowsAttestation MakeCredential(IntPtr window, WindowsMakeCredentialCall call, CancellationToken cancellationToken)
    {
        Record(window, cancellationToken);
        MakeCredentialCall = call;
        return Attestation;
    }

    public WindowsAssertion GetAssertion(IntPtr window, WindowsGetAssertionCall call, CancellationToken cancellationToken)
    {
        Record(window, cancellationToken);
        GetAssertionCall = call;
        return Assertion;
    }

    private void Record(IntPtr window, CancellationToken cancellationToken)
    {
        Calls++;
        Window = window;
        DuringCall?.Invoke();
        TokenWasCancelledDuringCall = cancellationToken.IsCancellationRequested;
        if (CallFailure is not null)
        {
            throw CallFailure;
        }
    }
}
