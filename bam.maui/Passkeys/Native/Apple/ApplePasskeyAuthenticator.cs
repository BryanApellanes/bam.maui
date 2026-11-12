using AuthenticationServices;
using Foundation;
using LocalAuthentication;
using Microsoft.Maui.ApplicationModel;
using UIKit;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Runs passkey ceremonies through Apple's AuthenticationServices on iOS and Mac Catalyst, with
/// passkeys held by the device (iCloud Keychain or another credential provider).  The system
/// shows the sheet and writes the client data; this type passes the parsed options in and builds
/// standard WebAuthn JSON from the raw result.
/// <para>
/// The system only serves an app whose Associated Domains entitlement lists
/// <c>webcredentials:</c> plus the relying party id, and whose id appears in that domain's
/// <c>/.well-known/apple-app-site-association</c>.  Without both, a ceremony fails with
/// <see cref="PasskeyErrorCategory.PlatformFailure"/>.
/// </para>
/// <para>
/// Not covered in this phase: security keys, and <c>excludeCredentials</c>, which the .NET
/// binding in use does not expose for platform passkeys.  The relying party still refuses a
/// duplicate when it verifies the registration.
/// </para>
/// </summary>
public sealed class ApplePasskeyAuthenticator : PlatformPasskeyAuthenticator
{
    private static readonly string[] PlatformTransports = { "internal" };

    // The controller's delegate is a weak reference and nothing else roots a ceremony while the sheet is up.
    private readonly HashSet<Ceremony> _running = new HashSet<Ceremony>();

    /// <summary>Creates the authenticator.</summary>
    /// <param name="configuration">The app's passkey configuration.</param>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    public ApplePasskeyAuthenticator(PasskeyConfiguration configuration) : base(configuration)
    {
    }

    /// <summary>
    /// Reports <see cref="PasskeyAvailability.UnsupportedOperatingSystem"/> below version 16 and
    /// <see cref="PasskeyAvailability.DeviceSecurityNotConfigured"/> on a device without a
    /// passcode.  Whether iCloud Keychain is on cannot be asked; the system explains that to the
    /// user itself when a ceremony starts.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The device's capabilities.</returns>
    public override Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string platform = OperatingSystem.IsMacCatalyst() ? "MacCatalyst" : "iOS";
        if (!IsVersion16())
        {
            return Task.FromResult(new PasskeyCapabilities { Availability = PasskeyAvailability.UnsupportedOperatingSystem, Platform = platform });
        }
        using LAContext context = new LAContext();
        if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out NSError? _))
        {
            return Task.FromResult(new PasskeyCapabilities { Availability = PasskeyAvailability.DeviceSecurityNotConfigured, Platform = platform });
        }
        return Task.FromResult(new PasskeyCapabilities
        {
            Availability = PasskeyAvailability.Available,
            SupportsDiscoverableCredentials = true,
            SupportsSecurityKeys = false,
            Platform = platform
        });
    }

    /// <inheritdoc />
    protected override async Task<WebAuthnRegistrationResponse> CreateCoreAsync(WebAuthnCreationRequest request, WebAuthnCreationOptions options, CancellationToken cancellationToken)
    {
        ASAuthorization authorization = await RunAsync(() =>
        {
            ASAuthorizationPlatformPublicKeyCredentialProvider provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(request.RelyingPartyId);
            ASAuthorizationPlatformPublicKeyCredentialRegistrationRequest registration = provider.CreateCredentialRegistrationRequest(NSData.FromArray(request.Challenge), request.UserName, NSData.FromArray(request.UserId));
            registration.DisplayName = request.UserDisplayName;
            registration.UserVerificationPreference = UserVerificationOf(request.UserVerification);
            registration.AttestationPreference = AttestationOf(request.Attestation);
            return registration;
        }, cancellationToken).ConfigureAwait(false);

        ASAuthorizationPlatformPublicKeyCredentialRegistration? credential = authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialRegistration>();
        if (credential is null)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The system returned no passkey registration.");
        }
        return WebAuthnResponseJson.Registration(
            credential.CredentialId?.ToArray(),
            credential.RawClientDataJson?.ToArray(),
            credential.RawAttestationObject?.ToArray(),
            PlatformTransports,
            WebAuthnResponseJson.PlatformAttachment);
    }

    /// <inheritdoc />
    protected override async Task<WebAuthnAssertionResponse> AssertCoreAsync(WebAuthnAssertionRequest request, WebAuthnRequestOptions options, CancellationToken cancellationToken)
    {
        ASAuthorization authorization = await RunAsync(() =>
        {
            ASAuthorizationPlatformPublicKeyCredentialProvider provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(request.RelyingPartyId);
            ASAuthorizationPlatformPublicKeyCredentialAssertionRequest assertion = provider.CreateCredentialAssertionRequest(NSData.FromArray(request.Challenge));
            // Both members are set through the protocol the request implements; the class's own copies were retired.
            IASAuthorizationPublicKeyCredentialAssertionRequest common = assertion;
            common.UserVerificationPreference = UserVerificationOf(request.UserVerification);
            if (request.AllowCredentials.Count > 0)
            {
                common.AllowedCredentials = request.AllowCredentials
                    .Select(descriptor => (IASAuthorizationPublicKeyCredentialDescriptor)new ASAuthorizationPlatformPublicKeyCredentialDescriptor(NSData.FromArray(descriptor.Id)))
                    .ToArray();
            }
            return assertion;
        }, cancellationToken).ConfigureAwait(false);

        ASAuthorizationPlatformPublicKeyCredentialAssertion? credential = authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialAssertion>();
        if (credential is null)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The system returned no passkey assertion.");
        }
        return WebAuthnResponseJson.Assertion(
            credential.CredentialId?.ToArray(),
            credential.RawClientDataJson?.ToArray(),
            credential.RawAuthenticatorData?.ToArray(),
            credential.Signature?.ToArray(),
            credential.UserId?.ToArray(),
            WebAuthnResponseJson.PlatformAttachment);
    }

    // AuthenticationServices is driven from the main thread: the request is built, presented and cancelled there.
    private async Task<ASAuthorization> RunAsync(Func<ASAuthorizationRequest> createRequest, CancellationToken cancellationToken)
    {
        Ceremony ceremony = new Ceremony();
        lock (_running)
        {
            _running.Add(ceremony);
        }
        try
        {
            // The token ends the wait by itself; the sheet is closed on the main thread afterwards.
            using PlatformCeremony<AppleOutcome> pending = ceremony.Listen(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await MainThread.InvokeOnMainThreadAsync(() => ceremony.Start(createRequest())).ConfigureAwait(false);
            AppleOutcome outcome = await pending.Completion.ConfigureAwait(false);
            if (outcome.Failure is not null)
            {
                throw outcome.Failure;
            }
            return outcome.Authorization!;
        }
        finally
        {
            lock (_running)
            {
                _running.Remove(ceremony);
            }
        }
    }

    private static bool IsVersion16()
    {
        return OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16);
    }

    private static NSString UserVerificationOf(string userVerification)
    {
        return userVerification switch
        {
            "required" => ASAuthorizationPublicKeyCredentialUserVerificationPreference.Required,
            "discouraged" => ASAuthorizationPublicKeyCredentialUserVerificationPreference.Discouraged,
            _ => ASAuthorizationPublicKeyCredentialUserVerificationPreference.Preferred
        };
    }

    private static NSString AttestationOf(string attestation)
    {
        return attestation switch
        {
            "direct" => ASAuthorizationPublicKeyCredentialAttestationKind.Direct,
            "indirect" => ASAuthorizationPublicKeyCredentialAttestationKind.Indirect,
            "enterprise" => ASAuthorizationPublicKeyCredentialAttestationKind.Enterprise,
            _ => ASAuthorizationPublicKeyCredentialAttestationKind.None
        };
    }

    private sealed record AppleOutcome(ASAuthorization? Authorization, PasskeyException? Failure);

    /// <summary>One sheet: the controller, its delegate and the pending result.  Start, Cancel and the delegate run on the main thread.</summary>
    private sealed class Ceremony : ASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        private PlatformCeremony<AppleOutcome>? _pending;
        private ASAuthorizationController? _controller;
        private UIWindow? _window;
        private bool _finished;

        public PlatformCeremony<AppleOutcome> Listen(CancellationToken cancellationToken)
        {
            _pending = new PlatformCeremony<AppleOutcome>(cancellationToken, () => MainThread.BeginInvokeOnMainThread(Cancel));
            return _pending;
        }

        public void Start(ASAuthorizationRequest request)
        {
            // A token that fired before the sheet was shown: show nothing.
            if (_pending is null || _pending.IsCompleted)
            {
                return;
            }
            _window = Platform.GetCurrentUIViewController()?.View?.Window;
            if (_window is null)
            {
                Finish(new AppleOutcome(null, new PasskeyException(PasskeyErrorCategory.PlatformFailure, "There is no window to show the passkey sheet over.")));
                return;
            }
            _controller = new ASAuthorizationController(new ASAuthorizationRequest[] { request });
            _controller.Delegate = this;
            _controller.PresentationContextProvider = this;
            _controller.PerformRequests();
        }

        public void Cancel()
        {
            // The pending result is already cancelled by now; this only closes a sheet that is still up.
            if (_controller is null || _finished)
            {
                return;
            }
            // ASAuthorizationController.cancel exists from version 16; the capability probe already requires that.
            if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16))
            {
                _controller.Cancel();
            }
        }

        public override void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
        {
            Finish(new AppleOutcome(authorization, null));
        }

        public override void DidComplete(ASAuthorizationController controller, NSError error)
        {
            // Domain and code only: the description can name the app and the relying party.
            PasskeyErrorCategory category = ApplePasskeyErrors.Categorize(error.Domain, (long)error.Code);
            Finish(new AppleOutcome(null, new PasskeyException(category, $"AuthenticationServices reported {error.Domain} code {(long)error.Code}.")));
        }

        private void Finish(AppleOutcome outcome)
        {
            _finished = true;
            _pending?.Report(outcome);
        }

        public UIWindow GetPresentationAnchor(ASAuthorizationController controller)
        {
            // Start refuses to present without a window, so this is set by the time the system asks.
            return _window!;
        }
    }
}
