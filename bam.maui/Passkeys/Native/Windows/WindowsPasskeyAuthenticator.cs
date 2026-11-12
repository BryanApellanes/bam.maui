using System.Runtime.Versioning;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Runs passkey ceremonies through Windows' own WebAuthn API (<c>webauthn.dll</c>, Windows 10
/// version 1903 and later).  Windows shows the prompt and talks to Windows Hello, a security key
/// or a phone; this type translates the relying party's options into the call and the result
/// back into standard WebAuthn JSON.
/// <para>
/// Windows leaves the client data to the caller, so this type writes it, with the origin
/// <c>https://</c> plus the configured relying party id.  Windows does not check that a desktop
/// app belongs to the relying party it names; that binding rests on the check every authenticator
/// here makes, that the options are for the configured relying party.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPasskeyAuthenticator : PlatformPasskeyAuthenticator
{
    /// <summary>The timeout handed to Windows when the options give none.</summary>
    public const int DefaultTimeoutMilliseconds = 60000;

    private const string PlatformName = "Windows";

    // WEBAUTHN_CTAP_TRANSPORT_* bits and their WebAuthn names.
    private static readonly (int Bit, string Name)[] Transports =
    {
        (0x01, "usb"),
        (0x02, "nfc"),
        (0x04, "ble"),
        (0x10, "internal"),
        (0x20, "hybrid"),
        (0x40, "smart-card")
    };

    private const int InternalTransport = 0x10;

    private readonly Func<IntPtr> _windowHandle;
    private readonly IWindowsWebAuthnApi _api;

    /// <summary>Creates the authenticator.</summary>
    /// <param name="configuration">The app's passkey configuration.</param>
    /// <param name="windowHandle">Returns the handle (<c>HWND</c>) of the window the prompt belongs to; called once per ceremony.</param>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    public WindowsPasskeyAuthenticator(PasskeyConfiguration configuration, Func<IntPtr> windowHandle)
        : this(configuration, windowHandle, new WindowsWebAuthnApi())
    {
    }

    internal WindowsPasskeyAuthenticator(PasskeyConfiguration configuration, Func<IntPtr> windowHandle, IWindowsWebAuthnApi api)
        : base(configuration)
    {
        ArgumentNullException.ThrowIfNull(windowHandle);
        ArgumentNullException.ThrowIfNull(api);
        _windowHandle = windowHandle;
        _api = api;
    }

    /// <summary>
    /// Reports <see cref="PasskeyAvailability.Available"/> when <c>webauthn.dll</c> is present,
    /// because a ceremony can then run with a security key or a phone even without Windows Hello.
    /// <see cref="PasskeyCapabilities.SupportsDiscoverableCredentials"/> is true only when Windows
    /// Hello is set up.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The device's capabilities.</returns>
    public override Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int version;
        try
        {
            version = _api.GetApiVersionNumber();
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
        {
            version = 0;
        }
        if (version < 1)
        {
            return Task.FromResult(new PasskeyCapabilities { Availability = PasskeyAvailability.UnsupportedOperatingSystem, Platform = PlatformName });
        }
        bool hello;
        try
        {
            hello = _api.IsUserVerifyingPlatformAuthenticatorAvailable();
        }
        catch (EntryPointNotFoundException)
        {
            hello = false;
        }
        return Task.FromResult(new PasskeyCapabilities
        {
            Availability = PasskeyAvailability.Available,
            SupportsDiscoverableCredentials = hello,
            SupportsSecurityKeys = true,
            Platform = PlatformName
        });
    }

    /// <inheritdoc />
    protected override async Task<WebAuthnRegistrationResponse> CreateCoreAsync(WebAuthnCreationRequest request, WebAuthnCreationOptions options, CancellationToken cancellationToken)
    {
        IntPtr window = RequireWindow();
        byte[] clientData = WebAuthnClientData.Write(WebAuthnClientData.RegistrationType, request.Challenge, WebAuthnClientData.OriginOf(RelyingPartyId));
        WindowsMakeCredentialCall call = new WindowsMakeCredentialCall
        {
            RelyingPartyId = request.RelyingPartyId,
            RelyingPartyName = request.RelyingPartyName,
            UserId = request.UserId,
            UserName = request.UserName,
            UserDisplayName = request.UserDisplayName,
            Algorithms = request.Algorithms,
            ClientDataJson = clientData,
            TimeoutMilliseconds = request.TimeoutMilliseconds ?? DefaultTimeoutMilliseconds,
            ExcludeCredentialIds = request.ExcludeCredentials.Select(descriptor => descriptor.Id).ToList(),
            AuthenticatorAttachment = AttachmentOf(request.AuthenticatorAttachment),
            RequireResidentKey = request.ResidentKey == "required",
            PreferResidentKey = request.ResidentKey == "preferred",
            UserVerificationRequirement = UserVerificationOf(request.UserVerification),
            AttestationConveyancePreference = AttestationOf(request.Attestation)
        };

        // The call blocks until the prompt closes, so it runs off the caller's thread.
        WindowsAttestation attestation = await Task.Run(() => Call(() => _api.MakeCredential(window, call, cancellationToken)), CancellationToken.None).ConfigureAwait(false);
        ThrowUnlessSucceeded(attestation.Result, true);
        return WebAuthnResponseJson.Registration(attestation.CredentialId, clientData, attestation.AttestationObject, TransportsOf(attestation.UsedTransport), AttachmentOf(attestation.UsedTransport));
    }

    /// <inheritdoc />
    protected override async Task<WebAuthnAssertionResponse> AssertCoreAsync(WebAuthnAssertionRequest request, WebAuthnRequestOptions options, CancellationToken cancellationToken)
    {
        IntPtr window = RequireWindow();
        byte[] clientData = WebAuthnClientData.Write(WebAuthnClientData.AssertionType, request.Challenge, WebAuthnClientData.OriginOf(RelyingPartyId));
        WindowsGetAssertionCall call = new WindowsGetAssertionCall
        {
            RelyingPartyId = request.RelyingPartyId,
            ClientDataJson = clientData,
            TimeoutMilliseconds = request.TimeoutMilliseconds ?? DefaultTimeoutMilliseconds,
            AllowCredentialIds = request.AllowCredentials.Select(descriptor => descriptor.Id).ToList(),
            UserVerificationRequirement = UserVerificationOf(request.UserVerification)
        };

        WindowsAssertion assertion = await Task.Run(() => Call(() => _api.GetAssertion(window, call, cancellationToken)), CancellationToken.None).ConfigureAwait(false);
        ThrowUnlessSucceeded(assertion.Result, false);
        // The library does not say which transport answered a sign-in at this structure version, so no attachment is claimed.
        return WebAuthnResponseJson.Assertion(assertion.CredentialId, clientData, assertion.AuthenticatorData, assertion.Signature, assertion.UserId, null);
    }

    private IntPtr RequireWindow()
    {
        IntPtr window = _windowHandle();
        if (window == IntPtr.Zero)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "There is no window to show the Windows passkey prompt over.");
        }
        return window;
    }

    private static TResult Call<TResult>(Func<TResult> call)
    {
        try
        {
            return call();
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
        {
            throw new PasskeyException(PasskeyErrorCategory.UnsupportedOperatingSystem, "This version of Windows has no WebAuthn API.", exception);
        }
    }

    private static void ThrowUnlessSucceeded(int result, bool creating)
    {
        PasskeyErrorCategory category = WindowsPasskeyErrors.Categorize(result, creating);
        if (category != PasskeyErrorCategory.None)
        {
            throw new PasskeyException(category, $"webauthn.dll returned 0x{result:X8}.");
        }
    }

    private static int AttachmentOf(string? attachment)
    {
        return attachment switch
        {
            WebAuthnResponseJson.PlatformAttachment => 1,
            WebAuthnResponseJson.CrossPlatformAttachment => 2,
            _ => 0
        };
    }

    private static int UserVerificationOf(string userVerification)
    {
        return userVerification switch
        {
            "required" => 1,
            "discouraged" => 3,
            _ => 2
        };
    }

    private static int AttestationOf(string attestation)
    {
        return attestation switch
        {
            "none" => 1,
            "indirect" => 2,
            "direct" => 3,
            // "enterprise" and anything unknown: ask for none rather than leaving it to Windows.
            _ => 1
        };
    }

    private static IReadOnlyList<string> TransportsOf(int usedTransport)
    {
        List<string> names = new List<string>();
        foreach ((int Bit, string Name) transport in Transports)
        {
            if ((usedTransport & transport.Bit) != 0)
            {
                names.Add(transport.Name);
            }
        }
        return names;
    }

    private static string? AttachmentOf(int usedTransport)
    {
        if (usedTransport == 0)
        {
            return null;
        }
        return (usedTransport & InternalTransport) != 0 ? WebAuthnResponseJson.PlatformAttachment : WebAuthnResponseJson.CrossPlatformAttachment;
    }
}
