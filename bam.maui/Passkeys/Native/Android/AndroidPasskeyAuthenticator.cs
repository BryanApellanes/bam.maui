using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.Content;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using AndroidX.Credentials.Exceptions.PublicKeyCredential;
using Java.Util.Concurrent;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Runs passkey ceremonies through Android's Credential Manager (<c>androidx.credentials</c>).
/// Credential Manager takes the relying party's options as WebAuthn JSON and returns the response
/// as WebAuthn JSON, so nothing is rebuilt here: the options are checked and passed on whole.
/// <para>
/// This phase covers Android 14 (API 34) and later, where Credential Manager is part of the
/// platform.  Android verifies the app against the relying party's
/// <c>/.well-known/assetlinks.json</c>, and the origin the relying party sees is
/// <c>android:apk-key-hash:</c> plus the app's signing certificate hash.
/// </para>
/// </summary>
public sealed class AndroidPasskeyAuthenticator : PlatformPasskeyAuthenticator
{
    /// <summary>The first API level this authenticator serves (Android 14).</summary>
    public const int MinimumApiLevel = 34;

    private const string PlatformName = "Android";

    /// <summary>Creates the authenticator.</summary>
    /// <param name="configuration">The app's passkey configuration.</param>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    public AndroidPasskeyAuthenticator(PasskeyConfiguration configuration) : base(configuration)
    {
    }

    /// <summary>
    /// Reports <see cref="PasskeyAvailability.UnsupportedOperatingSystem"/> below Android 14 and
    /// <see cref="PasskeyAvailability.DeviceSecurityNotConfigured"/> without a screen lock.
    /// Whether a credential provider is enabled cannot be asked without prompting; a ceremony on
    /// a device without one fails with <see cref="PasskeyErrorCategory.MissingCredentialProvider"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The device's capabilities.</returns>
    public override Task<PasskeyCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            return Task.FromResult(new PasskeyCapabilities { Availability = PasskeyAvailability.UnsupportedOperatingSystem, Platform = PlatformName });
        }
        KeyguardManager? keyguard = Android.App.Application.Context.GetSystemService(Context.KeyguardService) as KeyguardManager;
        if (keyguard is null || !keyguard.IsDeviceSecure)
        {
            return Task.FromResult(new PasskeyCapabilities { Availability = PasskeyAvailability.DeviceSecurityNotConfigured, Platform = PlatformName });
        }
        return Task.FromResult(new PasskeyCapabilities
        {
            Availability = PasskeyAvailability.Available,
            SupportsDiscoverableCredentials = true,
            SupportsSecurityKeys = true,
            Platform = PlatformName
        });
    }

    /// <inheritdoc />
    protected override async Task<WebAuthnRegistrationResponse> CreateCoreAsync(WebAuthnCreationRequest request, WebAuthnCreationOptions options, CancellationToken cancellationToken)
    {
        Activity activity = RequireActivity();
        using CancellationSignal signal = new CancellationSignal();
        using PlatformCeremony<CeremonyOutcome> ceremony = new PlatformCeremony<CeremonyOutcome>(cancellationToken, signal.Cancel);
        // A token that fired already never reaches Credential Manager.
        cancellationToken.ThrowIfCancellationRequested();
        ICredentialManager manager = CredentialManager.Create(activity);
        manager.CreateCredentialAsync(activity, new CreatePublicKeyCredentialRequest(options.Json), signal, MainExecutor(activity), new CeremonyCallback(ceremony));

        Java.Lang.Object? result = await Await(ceremony, true).ConfigureAwait(false);
        CreatePublicKeyCredentialResponse? response = Cast<CreatePublicKeyCredentialResponse>(result);
        if (response is not null && !string.IsNullOrWhiteSpace(response.RegistrationResponseJson))
        {
            return new WebAuthnRegistrationResponse(response.RegistrationResponseJson);
        }
        throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "Credential Manager returned no public key credential.");
    }

    /// <inheritdoc />
    protected override async Task<WebAuthnAssertionResponse> AssertCoreAsync(WebAuthnAssertionRequest request, WebAuthnRequestOptions options, CancellationToken cancellationToken)
    {
        Activity activity = RequireActivity();
        using CancellationSignal signal = new CancellationSignal();
        using PlatformCeremony<CeremonyOutcome> ceremony = new PlatformCeremony<CeremonyOutcome>(cancellationToken, signal.Cancel);
        cancellationToken.ThrowIfCancellationRequested();
        List<CredentialOption> credentialOptions = new List<CredentialOption> { new GetPublicKeyCredentialOption(options.Json) };
        ICredentialManager manager = CredentialManager.Create(activity);
        manager.GetCredentialAsync(activity, new GetCredentialRequest(credentialOptions), signal, MainExecutor(activity), new CeremonyCallback(ceremony));

        Java.Lang.Object? result = await Await(ceremony, false).ConfigureAwait(false);
        PublicKeyCredential? credential = Cast<PublicKeyCredential>(Cast<GetCredentialResponse>(result)?.Credential);
        if (credential is not null && !string.IsNullOrWhiteSpace(credential.AuthenticationResponseJson))
        {
            return new WebAuthnAssertionResponse(credential.AuthenticationResponseJson);
        }
        throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "Credential Manager returned no public key credential.");
    }

    private static async Task<Java.Lang.Object?> Await(PlatformCeremony<CeremonyOutcome> ceremony, bool creating)
    {
        // Throws OperationCanceledException when the caller's token fired first.
        CeremonyOutcome outcome = await ceremony.Completion.ConfigureAwait(false);
        if (!outcome.Failed)
        {
            return outcome.Value;
        }
        // The exception's type string is Credential Manager's stable contract (the class name is
        // the app's R8 settings' to change).  The class name is only a fallback for an error that
        // is not a Credential Manager exception.  Never the message: it can quote the request.
        string? errorType = ErrorTypeOf(outcome.Value, creating);
        PasskeyErrorCategory category;
        string reported;
        if (errorType is not null)
        {
            category = AndroidPasskeyErrors.CategorizeType(errorType);
            reported = AndroidPasskeyErrors.Printable(errorType);
        }
        else
        {
            string? exceptionClass = outcome.Value?.Class?.Name;
            category = AndroidPasskeyErrors.Categorize(exceptionClass, DomErrorClassOf(outcome.Value, exceptionClass));
            reported = exceptionClass ?? "an unknown error";
        }
        throw new PasskeyException(category, $"Credential Manager reported {reported} while {(creating ? "creating a passkey" : "signing in")}.");
    }

    // The callback's error arrives typed as a plain Java object; it is cast to the exception base the ceremony reports.
    private static string? ErrorTypeOf(Java.Lang.Object? error, bool creating)
    {
        if (error is null)
        {
            return null;
        }
        try
        {
            return creating
                ? Android.Runtime.Extensions.JavaCast<CreateCredentialException>(error)?.Type
                : Android.Runtime.Extensions.JavaCast<GetCredentialException>(error)?.Type;
        }
        catch (Exception exception) when (exception is InvalidCastException or Java.Lang.ClassCastException)
        {
            return null;
        }
    }

    // Fallback only: casts to the DOM exception by its Java class.
    private static string? DomErrorClassOf(Java.Lang.Object? error, string? exceptionClass)
    {
        if (error is null)
        {
            return null;
        }
        try
        {
            if (exceptionClass == AndroidPasskeyErrors.CreateDomException)
            {
                return Android.Runtime.Extensions.JavaCast<CreatePublicKeyCredentialDomException>(error)?.DomError?.Class?.Name;
            }
            if (exceptionClass == AndroidPasskeyErrors.GetDomException)
            {
                return Android.Runtime.Extensions.JavaCast<GetPublicKeyCredentialDomException>(error)?.DomError?.Class?.Name;
            }
        }
        catch (InvalidCastException)
        {
            // Not the class its name said; the exception is categorized without a DOM error.
        }
        return null;
    }

    private static TResult? Cast<TResult>(Java.Lang.Object? value) where TResult : class, Android.Runtime.IJavaObject
    {
        if (value is null)
        {
            return null;
        }
        try
        {
            return Android.Runtime.Extensions.JavaCast<TResult>(value);
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static Activity RequireActivity()
    {
        Activity? activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "There is no current activity to show the passkey prompt over.");
        }
        return activity;
    }

    private static IExecutor MainExecutor(Activity activity)
    {
        return ContextCompat.GetMainExecutor(activity) ?? throw new PasskeyException(PasskeyErrorCategory.PlatformFailure, "The activity has no main executor.");
    }

    private sealed record CeremonyOutcome(bool Failed, Java.Lang.Object? Value);

    private sealed class CeremonyCallback : Java.Lang.Object, ICredentialManagerCallback
    {
        private readonly PlatformCeremony<CeremonyOutcome> _ceremony;

        public CeremonyCallback(PlatformCeremony<CeremonyOutcome> ceremony)
        {
            _ceremony = ceremony;
        }

        public void OnError(Java.Lang.Object e)
        {
            _ceremony.Report(new CeremonyOutcome(true, e));
        }

        public void OnResult(Java.Lang.Object? result)
        {
            _ceremony.Report(new CeremonyOutcome(false, result));
        }
    }
}
