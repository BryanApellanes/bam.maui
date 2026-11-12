using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Platform passkey errors Should", Selector = "pkpe")]
public class PlatformPasskeyErrorsShould : UnitTestMenuContainer
{
    private const string Exceptions = "androidx.credentials.exceptions.";
    private const string DomErrors = "androidx.credentials.exceptions.domerrors.";

    public PlatformPasskeyErrorsShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void CategorizeAppleErrors()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("categorizes ASAuthorizationError codes and other domains", (unused, reg) =>
        {
            string domain = "com.apple.AuthenticationServices.AuthorizationError";
            List<long> otherCodes = new List<long>();
            foreach (long code in new long[] { 1000, 1002, 1003, 1004, 1005, 0, -1, 9999 })
            {
                if (ApplePasskeyErrors.Categorize(domain, code) != PasskeyErrorCategory.PlatformFailure)
                {
                    otherCodes.Add(code);
                }
            }
            return new AppleOutcome(
                ApplePasskeyErrors.Categorize(domain, 1001),
                ApplePasskeyErrors.Categorize(domain, 1006),
                string.Join(",", otherCodes),
                ApplePasskeyErrors.Categorize("NSCocoaErrorDomain", 1001),
                ApplePasskeyErrors.Categorize(null, 1001));
        })
        .TheTest
        .ShouldPass<AppleOutcome>((because, outcome) =>
        {
            because.ItsTrue("canceled is user cancelled", outcome.Canceled == PasskeyErrorCategory.UserCancelled);
            because.ItsTrue("a matched excluded credential is already registered", outcome.MatchedExcluded == PasskeyErrorCategory.CredentialAlreadyRegistered);
            because.ItsTrue("every other code is a platform failure; not: " + outcome.OtherCodes, outcome.OtherCodes.Length == 0);
            because.ItsTrue("code 1001 in another domain is not a cancellation", outcome.OtherDomain == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("no domain is a platform failure", outcome.NoDomain == PasskeyErrorCategory.PlatformFailure);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void CategorizeAndroidErrors()
    {
        After.Setup(reg => reg.Set(new CallLog()))
        .When<CallLog>("categorizes Credential Manager exceptions by Java class name", (unused, reg) =>
        {
            Dictionary<string, PasskeyErrorCategory> plain = new Dictionary<string, PasskeyErrorCategory>
            {
                [Exceptions + "CreateCredentialCancellationException"] = PasskeyErrorCategory.UserCancelled,
                [Exceptions + "GetCredentialCancellationException"] = PasskeyErrorCategory.UserCancelled,
                [Exceptions + "NoCredentialException"] = PasskeyErrorCategory.NoCredentials,
                [Exceptions + "CreateCredentialProviderConfigurationException"] = PasskeyErrorCategory.MissingCredentialProvider,
                [Exceptions + "CreateCredentialNoCreateOptionException"] = PasskeyErrorCategory.MissingCredentialProvider,
                [Exceptions + "GetCredentialProviderConfigurationException"] = PasskeyErrorCategory.MissingCredentialProvider,
                [Exceptions + "CreateCredentialUnsupportedException"] = PasskeyErrorCategory.UnsupportedOperatingSystem,
                [Exceptions + "GetCredentialUnsupportedException"] = PasskeyErrorCategory.UnsupportedOperatingSystem,
                [Exceptions + "CreateCredentialInterruptedException"] = PasskeyErrorCategory.PlatformFailure,
                [Exceptions + "GetCredentialInterruptedException"] = PasskeyErrorCategory.PlatformFailure,
                [Exceptions + "CreateCredentialUnknownException"] = PasskeyErrorCategory.PlatformFailure,
                [Exceptions + "GetCredentialUnknownException"] = PasskeyErrorCategory.PlatformFailure,
                ["java.lang.IllegalStateException"] = PasskeyErrorCategory.PlatformFailure,
                ["CreateCredentialCancellationException"] = PasskeyErrorCategory.PlatformFailure
            };
            Dictionary<string, PasskeyErrorCategory> dom = new Dictionary<string, PasskeyErrorCategory>
            {
                ["TimeoutError"] = PasskeyErrorCategory.ExpiredChallenge,
                ["DataError"] = PasskeyErrorCategory.InvalidOptions,
                ["ConstraintError"] = PasskeyErrorCategory.InvalidOptions,
                ["NotSupportedError"] = PasskeyErrorCategory.InvalidOptions,
                ["SyntaxError"] = PasskeyErrorCategory.InvalidOptions,
                ["EncodingError"] = PasskeyErrorCategory.InvalidOptions,
                ["NotAllowedError"] = PasskeyErrorCategory.PlatformFailure,
                ["SecurityError"] = PasskeyErrorCategory.PlatformFailure,
                ["UnknownError"] = PasskeyErrorCategory.PlatformFailure,
                ["AbortError"] = PasskeyErrorCategory.PlatformFailure
            };
            List<string> wrong = new List<string>();
            foreach (KeyValuePair<string, PasskeyErrorCategory> expected in plain)
            {
                if (AndroidPasskeyErrors.Categorize(expected.Key, null) != expected.Value)
                {
                    wrong.Add(expected.Key);
                }
            }
            foreach (KeyValuePair<string, PasskeyErrorCategory> expected in dom)
            {
                if (AndroidPasskeyErrors.Categorize(AndroidPasskeyErrors.CreateDomException, DomErrors + expected.Key) != expected.Value)
                {
                    wrong.Add("create " + expected.Key);
                }
                if (AndroidPasskeyErrors.Categorize(AndroidPasskeyErrors.GetDomException, DomErrors + expected.Key) != expected.Value)
                {
                    wrong.Add("get " + expected.Key);
                }
            }
            return new AndroidOutcome(
                string.Join(", ", wrong),
                AndroidPasskeyErrors.Categorize(AndroidPasskeyErrors.CreateDomException, DomErrors + "InvalidStateError"),
                AndroidPasskeyErrors.Categorize(AndroidPasskeyErrors.GetDomException, DomErrors + "InvalidStateError"),
                AndroidPasskeyErrors.Categorize(AndroidPasskeyErrors.CreateDomException, null),
                AndroidPasskeyErrors.Categorize(null, null),
                // A DOM error name on an exception that is not a DOM exception is ignored.
                AndroidPasskeyErrors.Categorize(Exceptions + "CreateCredentialUnknownException", DomErrors + "TimeoutError"),
                AndroidPasskeyErrors.CreateDomException + "|" + AndroidPasskeyErrors.GetDomException);
        })
        .TheTest
        .ShouldPass<AndroidOutcome>((because, outcome) =>
        {
            because.ItsTrue("every exception lands in its category; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("InvalidStateError while creating is already registered", outcome.InvalidStateCreating == PasskeyErrorCategory.CredentialAlreadyRegistered);
            because.ItsTrue("InvalidStateError while signing in is a platform failure", outcome.InvalidStateSigningIn == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("a DOM exception without an error is a platform failure", outcome.DomWithoutError == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("no class name is a platform failure", outcome.NoClass == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("a DOM error on another exception is ignored", outcome.DomErrorOnPlainException == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("the DOM exception class names are the library's: " + outcome.DomExceptionNames,
                outcome.DomExceptionNames == "androidx.credentials.exceptions.publickeycredential.CreatePublicKeyCredentialDomException|androidx.credentials.exceptions.publickeycredential.GetPublicKeyCredentialDomException");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void MapAndroidErrorsByType()
    {
        After.Setup(reg => reg.Set(new PasskeyMessages()))
        .When<PasskeyMessages>("categorizes each Credential Manager type string", (unused, reg) =>
        {
            // The strings are the ones in androidx.credentials 1.5.0's classes (Xamarin.AndroidX.Credentials 1.5.0.3).
            Dictionary<string, PasskeyErrorCategory> types = new Dictionary<string, PasskeyErrorCategory>
            {
                ["android.credentials.CreateCredentialException.TYPE_USER_CANCELED"] = PasskeyErrorCategory.UserCancelled,
                ["android.credentials.GetCredentialException.TYPE_USER_CANCELED"] = PasskeyErrorCategory.UserCancelled,
                ["android.credentials.GetCredentialException.TYPE_NO_CREDENTIAL"] = PasskeyErrorCategory.NoCredentials,
                ["android.credentials.CreateCredentialException.TYPE_NO_CREATE_OPTIONS"] = PasskeyErrorCategory.MissingCredentialProvider,
                ["androidx.credentials.TYPE_CREATE_CREDENTIAL_PROVIDER_CONFIGURATION_EXCEPTION"] = PasskeyErrorCategory.MissingCredentialProvider,
                ["androidx.credentials.TYPE_GET_CREDENTIAL_PROVIDER_CONFIGURATION_EXCEPTION"] = PasskeyErrorCategory.MissingCredentialProvider,
                ["androidx.credentials.TYPE_CREATE_CREDENTIAL_UNSUPPORTED_EXCEPTION"] = PasskeyErrorCategory.UnsupportedOperatingSystem,
                ["androidx.credentials.TYPE_GET_CREDENTIAL_UNSUPPORTED_EXCEPTION"] = PasskeyErrorCategory.UnsupportedOperatingSystem,
                ["android.credentials.CreateCredentialException.TYPE_INTERRUPTED"] = PasskeyErrorCategory.PlatformFailure,
                ["android.credentials.GetCredentialException.TYPE_UNKNOWN"] = PasskeyErrorCategory.PlatformFailure,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_INVALID_STATE_ERROR"] = PasskeyErrorCategory.CredentialAlreadyRegistered,
                ["androidx.credentials.TYPE_GET_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_INVALID_STATE_ERROR"] = PasskeyErrorCategory.PlatformFailure,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_TIMEOUT_ERROR"] = PasskeyErrorCategory.ExpiredChallenge,
                ["androidx.credentials.TYPE_GET_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_TIMEOUT_ERROR"] = PasskeyErrorCategory.ExpiredChallenge,
                ["androidx.credentials.TYPE_GET_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_DATA_ERROR"] = PasskeyErrorCategory.InvalidOptions,
                ["androidx.credentials.TYPE_GET_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_SYNTAX_ERROR"] = PasskeyErrorCategory.InvalidOptions,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_NOT_SUPPORTED_ERROR"] = PasskeyErrorCategory.InvalidOptions,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_CONSTRAINT_ERROR"] = PasskeyErrorCategory.InvalidOptions,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_ENCODING_ERROR"] = PasskeyErrorCategory.InvalidOptions,
                // NotAllowedError is not a cancellation: providers use it for failures the user did not choose.
                ["androidx.credentials.TYPE_GET_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_NOT_ALLOWED_ERROR"] = PasskeyErrorCategory.PlatformFailure,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION/androidx.credentials.TYPE_ABORT_ERROR"] = PasskeyErrorCategory.PlatformFailure,
                ["androidx.credentials.TYPE_CREATE_PUBLIC_KEY_CREDENTIAL_DOM_EXCEPTION"] = PasskeyErrorCategory.PlatformFailure,
                ["com.example.provider.CUSTOM_TYPE"] = PasskeyErrorCategory.PlatformFailure,
                [string.Empty] = PasskeyErrorCategory.PlatformFailure,
            };
            List<string> wrong = new List<string>();
            foreach (KeyValuePair<string, PasskeyErrorCategory> expected in types)
            {
                if (AndroidPasskeyErrors.CategorizeType(expected.Key) != expected.Value)
                {
                    wrong.Add(expected.Key);
                }
            }
            return new AndroidTypeOutcome(
                string.Join(", ", wrong),
                AndroidPasskeyErrors.CategorizeType(null),
                AndroidPasskeyErrors.Printable("android.credentials.GetCredentialException.TYPE_NO_CREDENTIAL"),
                AndroidPasskeyErrors.Printable("com.example.provider.ACCOUNT ada@example.com"),
                AndroidPasskeyErrors.Printable("androidx.credentials.TYPE_X ada@example.com"),
                AndroidPasskeyErrors.Printable(null));
        })
        .TheTest
        .ShouldPass<AndroidTypeOutcome>((because, outcome) =>
        {
            because.ItsTrue("every type lands in its category; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("no type is a platform failure", outcome.NoType == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("Credential Manager's own type prints as it is", outcome.OwnPrinted == "android.credentials.GetCredentialException.TYPE_NO_CREDENTIAL");
            because.ItsTrue("a provider's custom type does not print", outcome.CustomPrinted == "a custom type");
            because.ItsTrue("an own-prefixed type with other characters does not print", outcome.OddPrinted == "a custom type");
            because.ItsTrue("no type prints as a custom type", outcome.NullPrinted == "a custom type");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void GiveTheNewCategoryAMessage()
    {
        After.Setup(reg => reg.Set(new PasskeyMessages()))
        .When<PasskeyMessages>("reads the message for an already registered credential", (messages, reg) =>
        {
            PasskeyMessages blank = new PasskeyMessages { CredentialAlreadyRegistered = " " };
            bool blankRefused = false;
            try
            {
                blank.Validate();
            }
            catch (ArgumentException)
            {
                blankRefused = true;
            }
            PasskeyError error = new PasskeyErrorNormalizer(messages).Normalize(new PasskeyException(PasskeyErrorCategory.CredentialAlreadyRegistered));
            return new MessageOutcome(messages.For(PasskeyErrorCategory.CredentialAlreadyRegistered), messages.Unknown, blankRefused, error.Category, error.IsCancellation);
        })
        .TheTest
        .ShouldPass<MessageOutcome>((because, outcome) =>
        {
            because.ItsTrue("the category has its own message: " + outcome.Message, outcome.Message.Length > 0 && outcome.Message != outcome.Unknown);
            because.ItsTrue("a blank message is refused", outcome.BlankRefused);
            because.ItsTrue("the normalizer keeps the category", outcome.Normalized == PasskeyErrorCategory.CredentialAlreadyRegistered);
            because.ItsTrue("it is not a cancellation", !outcome.IsCancellation);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    private sealed record AppleOutcome(PasskeyErrorCategory Canceled, PasskeyErrorCategory MatchedExcluded, string OtherCodes, PasskeyErrorCategory OtherDomain, PasskeyErrorCategory NoDomain);

    private sealed record AndroidOutcome(string Wrong, PasskeyErrorCategory InvalidStateCreating, PasskeyErrorCategory InvalidStateSigningIn, PasskeyErrorCategory DomWithoutError, PasskeyErrorCategory NoClass, PasskeyErrorCategory DomErrorOnPlainException, string DomExceptionNames);

    private sealed record AndroidTypeOutcome(string Wrong, PasskeyErrorCategory NoType, string OwnPrinted, string CustomPrinted, string OddPrinted, string NullPrinted);

    private sealed record MessageOutcome(string Message, string Unknown, bool BlankRefused, PasskeyErrorCategory Normalized, bool IsCancellation);
}
