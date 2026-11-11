namespace Bam.Maui.Passkeys;

/// <summary>
/// Maps the result codes of Windows <c>webauthn.dll</c> onto <see cref="PasskeyErrorCategory"/>.
/// Only a code that means the user dismissed the prompt is <see cref="PasskeyErrorCategory.UserCancelled"/>;
/// the platform's own timeout is <see cref="PasskeyErrorCategory.ExpiredChallenge"/>.
/// </summary>
public static class WindowsPasskeyErrors
{
    /// <summary><c>S_OK</c>.</summary>
    public const int Success = 0;

    /// <summary><c>NTE_EXISTS</c>: the authenticator already holds a credential from the exclude list.</summary>
    public const int Exists = unchecked((int)0x8009000F);

    /// <summary><c>NTE_NOT_FOUND</c>: no credential fits the request.</summary>
    public const int NotFound = unchecked((int)0x80090011);

    /// <summary><c>NTE_TOKEN_KEYSET_STORAGE_FULL</c>: the authenticator has no room for another credential.</summary>
    public const int StorageFull = unchecked((int)0x80090023);

    /// <summary><c>NTE_INVALID_PARAMETER</c>.</summary>
    public const int InvalidParameter = unchecked((int)0x80090027);

    /// <summary><c>NTE_NOT_SUPPORTED</c>.</summary>
    public const int NotSupported = unchecked((int)0x80090029);

    /// <summary><c>NTE_DEVICE_NOT_FOUND</c>: no authenticator answered.</summary>
    public const int DeviceNotFound = unchecked((int)0x80090035);

    /// <summary><c>NTE_USER_CANCELLED</c>.</summary>
    public const int UserCancelled = unchecked((int)0x80090036);

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED)</c>.</summary>
    public const int Win32NotSupported = unchecked((int)0x80070032);

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_CANCELLED)</c>.</summary>
    public const int Win32Cancelled = unchecked((int)0x800704C7);

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_TIMEOUT)</c>.</summary>
    public const int Win32Timeout = unchecked((int)0x800705B4);

    /// <summary>The category for a result code.</summary>
    /// <param name="result">The <c>HRESULT</c> a <c>webauthn.dll</c> call returned.</param>
    /// <param name="creating">True for a registration ceremony, false for a sign-in.</param>
    /// <returns>The category; <see cref="PasskeyErrorCategory.None"/> for <see cref="Success"/>.</returns>
    public static PasskeyErrorCategory Categorize(int result, bool creating)
    {
        switch (result)
        {
            case Success:
                return PasskeyErrorCategory.None;
            case UserCancelled:
            case Win32Cancelled:
                return PasskeyErrorCategory.UserCancelled;
            case Win32Timeout:
                return PasskeyErrorCategory.ExpiredChallenge;
            case Exists:
                return creating ? PasskeyErrorCategory.CredentialAlreadyRegistered : PasskeyErrorCategory.PlatformFailure;
            case NotFound:
            case DeviceNotFound:
                // When signing in these mean nothing on or near the device fits; when creating, that no authenticator took the request.
                return creating ? PasskeyErrorCategory.PlatformFailure : PasskeyErrorCategory.NoCredentials;
            case InvalidParameter:
                return PasskeyErrorCategory.InvalidOptions;
            default:
                return PasskeyErrorCategory.PlatformFailure;
        }
    }
}

/// <summary>
/// Maps the <c>ASAuthorizationError</c> codes of Apple's AuthenticationServices onto
/// <see cref="PasskeyErrorCategory"/>.
/// </summary>
public static class ApplePasskeyErrors
{
    /// <summary>The <c>NSError</c> domain of <c>ASAuthorizationError</c>.</summary>
    public const string AuthorizationErrorDomain = "com.apple.AuthenticationServices.AuthorizationError";

    /// <summary><c>ASAuthorizationError.canceled</c>: the user dismissed the sheet.</summary>
    public const long Canceled = 1001;

    /// <summary><c>ASAuthorizationError.matchedExcludedCredential</c>: the device already holds a credential from the exclude list.</summary>
    public const long MatchedExcludedCredential = 1006;

    /// <summary>The category for an <c>NSError</c>.</summary>
    /// <param name="domain">The error's domain.</param>
    /// <param name="code">The error's code.</param>
    /// <returns>
    /// <see cref="PasskeyErrorCategory.UserCancelled"/> for a dismissed sheet,
    /// <see cref="PasskeyErrorCategory.CredentialAlreadyRegistered"/> for an excluded credential,
    /// and <see cref="PasskeyErrorCategory.PlatformFailure"/> for everything else, which includes
    /// an app whose associated domains do not cover the relying party.
    /// </returns>
    public static PasskeyErrorCategory Categorize(string? domain, long code)
    {
        if (!string.Equals(domain, AuthorizationErrorDomain, StringComparison.Ordinal))
        {
            return PasskeyErrorCategory.PlatformFailure;
        }
        return code switch
        {
            Canceled => PasskeyErrorCategory.UserCancelled,
            MatchedExcludedCredential => PasskeyErrorCategory.CredentialAlreadyRegistered,
            _ => PasskeyErrorCategory.PlatformFailure
        };
    }
}

/// <summary>
/// Maps what Android's Credential Manager reports onto <see cref="PasskeyErrorCategory"/>, by the
/// Java class name of the exception and, for a WebAuthn DOM exception, of the error it carries.
/// Only Credential Manager's cancellation exceptions are <see cref="PasskeyErrorCategory.UserCancelled"/>;
/// a WebAuthn <c>NotAllowedError</c> is not, because providers also use it for failures the user
/// did not choose.
/// </summary>
public static class AndroidPasskeyErrors
{
    /// <summary>The Java package of Credential Manager's exceptions.</summary>
    public const string ExceptionPackage = "androidx.credentials.exceptions.";

    /// <summary>The Java package of the WebAuthn DOM errors.</summary>
    public const string DomErrorPackage = "androidx.credentials.exceptions.domerrors.";

    /// <summary>The Java class of the DOM exception a registration reports.</summary>
    public const string CreateDomException = ExceptionPackage + "publickeycredential.CreatePublicKeyCredentialDomException";

    /// <summary>The Java class of the DOM exception a sign-in reports.</summary>
    public const string GetDomException = ExceptionPackage + "publickeycredential.GetPublicKeyCredentialDomException";

    /// <summary>The category for an exception.</summary>
    /// <param name="exceptionClassName">The Java class name of the exception passed to the callback.</param>
    /// <param name="domErrorClassName">The Java class name of its DOM error, when it is one of the two DOM exceptions.</param>
    /// <returns>The category; <see cref="PasskeyErrorCategory.PlatformFailure"/> for anything not listed.</returns>
    public static PasskeyErrorCategory Categorize(string? exceptionClassName, string? domErrorClassName)
    {
        switch (exceptionClassName)
        {
            case ExceptionPackage + "CreateCredentialCancellationException":
            case ExceptionPackage + "GetCredentialCancellationException":
                return PasskeyErrorCategory.UserCancelled;
            case ExceptionPackage + "NoCredentialException":
                return PasskeyErrorCategory.NoCredentials;
            case ExceptionPackage + "CreateCredentialProviderConfigurationException":
            case ExceptionPackage + "CreateCredentialNoCreateOptionException":
            case ExceptionPackage + "GetCredentialProviderConfigurationException":
                return PasskeyErrorCategory.MissingCredentialProvider;
            case ExceptionPackage + "CreateCredentialUnsupportedException":
            case ExceptionPackage + "GetCredentialUnsupportedException":
                return PasskeyErrorCategory.UnsupportedOperatingSystem;
            case CreateDomException:
                // InvalidStateError on creation is how WebAuthn says an excluded credential is already on the authenticator.
                return domErrorClassName == DomErrorPackage + "InvalidStateError" ? PasskeyErrorCategory.CredentialAlreadyRegistered : CategorizeDomError(domErrorClassName);
            case GetDomException:
                return CategorizeDomError(domErrorClassName);
            default:
                return PasskeyErrorCategory.PlatformFailure;
        }
    }

    private static PasskeyErrorCategory CategorizeDomError(string? domErrorClassName)
    {
        switch (domErrorClassName)
        {
            case DomErrorPackage + "TimeoutError":
                return PasskeyErrorCategory.ExpiredChallenge;
            case DomErrorPackage + "DataError":
            case DomErrorPackage + "ConstraintError":
            case DomErrorPackage + "NotSupportedError":
            case DomErrorPackage + "SyntaxError":
            case DomErrorPackage + "EncodingError":
                return PasskeyErrorCategory.InvalidOptions;
            default:
                return PasskeyErrorCategory.PlatformFailure;
        }
    }
}
