using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("Platform passkey errors Should", Selector = "pkpe")]
public class PlatformPasskeyErrorsShould : UnitTestMenuContainer
{
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
        After.Setup(reg => reg.Set(new PasskeyMessages()))
        .When<PasskeyMessages>("categorizes each Credential Manager exception and DOM error", (unused, reg) =>
        {
            Dictionary<AndroidCredentialError, PasskeyErrorCategory> kinds = new Dictionary<AndroidCredentialError, PasskeyErrorCategory>
            {
                [AndroidCredentialError.Cancelled] = PasskeyErrorCategory.UserCancelled,
                [AndroidCredentialError.NoCredential] = PasskeyErrorCategory.NoCredentials,
                [AndroidCredentialError.NoCreateOption] = PasskeyErrorCategory.MissingCredentialProvider,
                [AndroidCredentialError.ProviderConfiguration] = PasskeyErrorCategory.MissingCredentialProvider,
                [AndroidCredentialError.Unsupported] = PasskeyErrorCategory.UnsupportedOperatingSystem,
                [AndroidCredentialError.Other] = PasskeyErrorCategory.PlatformFailure,
            };
            Dictionary<AndroidDomError, PasskeyErrorCategory> dom = new Dictionary<AndroidDomError, PasskeyErrorCategory>
            {
                [AndroidDomError.Timeout] = PasskeyErrorCategory.ExpiredChallenge,
                [AndroidDomError.Data] = PasskeyErrorCategory.InvalidOptions,
                [AndroidDomError.Constraint] = PasskeyErrorCategory.InvalidOptions,
                [AndroidDomError.NotSupported] = PasskeyErrorCategory.InvalidOptions,
                [AndroidDomError.Syntax] = PasskeyErrorCategory.InvalidOptions,
                [AndroidDomError.Encoding] = PasskeyErrorCategory.InvalidOptions,
                // NotAllowedError and every other DOM error: not a cancellation, because providers use it for failures the user did not choose.
                [AndroidDomError.Other] = PasskeyErrorCategory.PlatformFailure,
            };
            List<string> wrong = new List<string>();
            foreach (KeyValuePair<AndroidCredentialError, PasskeyErrorCategory> expected in kinds)
            {
                foreach (AndroidDomError any in Enum.GetValues<AndroidDomError>())
                {
                    // A DOM error on an exception that is not a DOM exception is ignored.
                    if (AndroidPasskeyErrors.Categorize(expected.Key, any) != expected.Value)
                    {
                        wrong.Add(expected.Key + "/" + any);
                    }
                }
            }
            foreach (KeyValuePair<AndroidDomError, PasskeyErrorCategory> expected in dom)
            {
                if (AndroidPasskeyErrors.Categorize(AndroidCredentialError.CreateDomError, expected.Key) != expected.Value)
                {
                    wrong.Add("create " + expected.Key);
                }
                if (AndroidPasskeyErrors.Categorize(AndroidCredentialError.GetDomError, expected.Key) != expected.Value)
                {
                    wrong.Add("get " + expected.Key);
                }
            }
            return new AndroidOutcome(
                string.Join(", ", wrong),
                AndroidPasskeyErrors.Categorize(AndroidCredentialError.CreateDomError, AndroidDomError.InvalidState),
                AndroidPasskeyErrors.Categorize(AndroidCredentialError.GetDomError, AndroidDomError.InvalidState),
                Enum.GetValues<AndroidCredentialError>().Length);
        })
        .TheTest
        .ShouldPass<AndroidOutcome>((because, outcome) =>
        {
            because.ItsTrue("every exception and DOM error lands in its category; wrong: " + outcome.Wrong, outcome.Wrong.Length == 0);
            because.ItsTrue("InvalidStateError while creating is already registered", outcome.InvalidStateCreating == PasskeyErrorCategory.CredentialAlreadyRegistered);
            because.ItsTrue("InvalidStateError while signing in is a platform failure", outcome.InvalidStateSigningIn == PasskeyErrorCategory.PlatformFailure);
            because.ItsTrue("the kinds are the eight the map knows", outcome.Kinds == 8);
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

    private sealed record AndroidOutcome(string Wrong, PasskeyErrorCategory InvalidStateCreating, PasskeyErrorCategory InvalidStateSigningIn, int Kinds);

    private sealed record MessageOutcome(string Message, string Unknown, bool BlankRefused, PasskeyErrorCategory Normalized, bool IsCancellation);
}
