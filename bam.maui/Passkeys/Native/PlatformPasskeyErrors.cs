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
/// Maps what Android's Credential Manager reports onto <see cref="PasskeyErrorCategory"/>.  The
/// primary map is <see cref="CategorizeType"/>, over the exception's <c>getType()</c> string, which
/// is Credential Manager's stable contract and survives R8 renaming the exception classes.
/// <see cref="Categorize"/>, over Java class names, is the fallback for an error that is not a
/// Credential Manager exception.  Only Credential Manager's cancellation is
/// <see cref="PasskeyErrorCategory.UserCancelled"/>; a WebAuthn <c>NotAllowedError</c> is not,
/// because providers also use it for failures the user did not choose.
/// </summary>
public static class AndroidPasskeyErrors
{
    /// <summary>The type of a registration the user cancelled.</summary>
    public const string CreateCancelledType = "android.credentials.CreateCredentialException.TYPE_USER_CANCELED";

    /// <summary>The type of a sign-in the user cancelled.</summary>
    public const string GetCancelledType = "android.credentials.GetCredentialException.TYPE_USER_CANCELED";

    /// <summary>The type of a sign-in with no matching credential.</summary>
    public const string NoCredentialType = "android.credentials.GetCredentialException.TYPE_NO_CREDENTIAL";

    /// <summary>The type of a registration no provider offered to store.</summary>
    public const string NoCreateOptionsType = "android.credentials.CreateCredentialException.TYPE_NO_CREATE_OPTIONS";

    /// <summary>The type of a registration with no provider configured.</summary>
    public const string CreateProviderConfigurationType = "androidx.credentials.TYPE_CREATE_CREDENTIAL_PROVIDER_CONFIGURATION_EXCEPTION";

    /// <summary>The type of a sign-in with no provider configured.</summary>
    public const string GetProviderConfigurationType = "androidx.credentials.TYPE_GET_CREDENTIAL_PROVIDER_CONFIGURATION_EXCEPTION";

    /// <summary>The type of a registration the device does not support.</summary>
    public const string CreateUnsupportedType = "androidx.credentials.TYPE_CREATE_CREDENTIAL_UNSUPPORTED_EXCEPTION";

    /// <summary>The type of a sign-in the device does not support.</summary>
    public const string GetUnsupportedType = "androidx.credentials.TYPE_GET_CREDENTIAL_UNSUPPORTED_EXCEPTION";

    /// <summary>The type of a registration's WebAuthn DOM exception; followed by <c>/</c> and the DOM error's type.</summary>
    public const string CreateDomExceptionType = "androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION";

    /// <summary>The type of a sign-in's WebAuthn DOM exception; followed by <c>/</c> and the DOM error's type.</summary>
    public const string GetDomExceptionType = "androidx.credentials.TYPE_GET_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION";

    /// <summary>The prefix of every DOM error type.</summary>
    public const string DomErrorTypePrefix = "androidx.credentials.TYPE_";

    /// <summary>The category for a Credential Manager exception's <c>getType()</c>.</summary>
    /// <param name="exceptionType">The exception's type string.</param>
    /// <returns>The category; <see cref="PasskeyErrorCategory.PlatformFailure"/> for anything not listed.</returns>
    public static PasskeyErrorCategory CategorizeType(string? exceptionType)
    {
        switch (exceptionType)
        {
            case CreateCancelledType:
            case GetCancelledType:
                return PasskeyErrorCategory.UserCancelled;
            case NoCredentialType:
                return PasskeyErrorCategory.NoCredentials;
            case NoCreateOptionsType:
            case CreateProviderConfigurationType:
            case GetProviderConfigurationType:
                return PasskeyErrorCategory.MissingCredentialProvider;
            case CreateUnsupportedType:
            case GetUnsupportedType:
                return PasskeyErrorCategory.UnsupportedOperatingSystem;
        }
        if (exceptionType is null)
        {
            return PasskeyErrorCategory.PlatformFailure;
        }
        if (exceptionType.StartsWith(CreateDomExceptionType + "/", StringComparison.Ordinal))
        {
            string domErrorType = exceptionType.Substring(CreateDomExceptionType.Length + 1);
            // InvalidStateError on creation is how WebAuthn says an excluded credential is already on the authenticator.
            return domErrorType == DomErrorTypePrefix + "INVALID_STATE_ERROR" ? PasskeyErrorCategory.CredentialAlreadyRegistered : CategorizeDomErrorType(domErrorType);
        }
        if (exceptionType.StartsWith(GetDomExceptionType + "/", StringComparison.Ordinal))
        {
            return CategorizeDomErrorType(exceptionType.Substring(GetDomExceptionType.Length + 1));
        }
        return PasskeyErrorCategory.PlatformFailure;
    }

    /// <summary>
    /// The type as it may appear in a diagnostic message: Credential Manager's own types as they
    /// are, anything else (a provider's custom type) as "a custom type".
    /// </summary>
    /// <param name="exceptionType">The exception's type string.</param>
    /// <returns>The printable form.</returns>
    public static string Printable(string? exceptionType)
    {
        bool own = exceptionType is not null
            && exceptionType.Length <= 160
            && (exceptionType.StartsWith("android.credentials.", StringComparison.Ordinal) || exceptionType.StartsWith("androidx.credentials.", StringComparison.Ordinal))
            && exceptionType.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '/');
        return own ? exceptionType! : "a custom type";
    }

    private static PasskeyErrorCategory CategorizeDomErrorType(string domErrorType)
    {
        switch (domErrorType)
        {
            case DomErrorTypePrefix + "TIMEOUT_ERROR":
                return PasskeyErrorCategory.ExpiredChallenge;
            case DomErrorTypePrefix + "DATA_ERROR":
            case DomErrorTypePrefix + "CONSTRAINT_ERROR":
            case DomErrorTypePrefix + "NOT_SUPPORTED_ERROR":
            case DomErrorTypePrefix + "SYNTAX_ERROR":
            case DomErrorTypePrefix + "ENCODING_ERROR":
                return PasskeyErrorCategory.InvalidOptions;
            default:
                return PasskeyErrorCategory.PlatformFailure;
        }
    }

    /// <summary>The Java package of Credential Manager's exceptions.</summary>
    public const string ExceptionPackage = "androidx.credentials.exceptions.";

    /// <summary>The Java package of the WebAuthn DOM errors.</summary>
    public const string DomErrorPackage = "androidx.credentials.exceptions.domerrors.";

    /// <summary>The Java class of the DOM exception a registration reports.</summary>
    public const string CreateDomException = ExceptionPackage + "publickeycredential.CreatePublicKeyCredentialDomException";

    /// <summary>The Java class of the DOM exception a sign-in reports.</summary>
    public const string GetDomException = ExceptionPackage + "publickeycredential.GetPublicKeyCredentialDomException";

    /// <summary>The fallback category for an exception, by Java class name; used only when the error is not a Credential Manager exception with a type.</summary>
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
