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

    private sealed record MessageOutcome(string Message, string Unknown, bool BlankRefused, PasskeyErrorCategory Normalized, bool IsCancellation);
}
