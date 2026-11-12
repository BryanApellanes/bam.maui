using Bam.DependencyInjection;
using Bam.Maui.Passkeys;
using Bam.Maui.Tests.TestClasses;
using Bam.Test;

namespace Bam.Maui.Tests.Unit;

[UnitTestMenu("PasskeyRecovery Should", Selector = "pkrc")]
public class PasskeyRecoveryShould : UnitTestMenuContainer
{
    public PasskeyRecoveryShould(ServiceRegistry serviceRegistry) : base(serviceRegistry) { }

    [UnitTest]
    public void PlanEveryCategoryForBothFlows()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("plans a failure of every category in sign-in and registration", (configuration, reg) =>
        {
            List<string> wrong = new List<string>();
            foreach (PasskeyErrorCategory category in Enum.GetValues<PasskeyErrorCategory>())
            {
                foreach (PasskeyFlow flow in Enum.GetValues<PasskeyFlow>())
                {
                    PasskeyRecoveryPlan plan = PasskeyRecovery.For(new PasskeyError(category, "said " + category), flow, configuration);
                    string expected = Expected(category, flow, configuration.Text);
                    string actual = Describe(plan);
                    if (expected != actual)
                    {
                        wrong.Add($"{flow}/{category}: expected [{expected}] got [{actual}]");
                    }
                }
            }
            return new EveryCategoryOutcome(string.Join("; ", wrong));
        })
        .TheTest
        .ShouldPass<EveryCategoryOutcome>((because, outcome) =>
        {
            because.ItsTrue("every category gets the planned screen: " + outcome.Wrong, outcome.Wrong.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void OfferTheAppsAlternativesWhenItHasThem()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("plans failures with an alternative label, with registration off, and with authentication off", (configuration, reg) =>
        {
            configuration.Experience.AlternativeSignInLabel = "Email me a link";
            PasskeyRecoveryPlan network = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.NetworkFailure, "offline"), PasskeyFlow.SignIn, configuration);
            PasskeyRecoveryPlan unavailable = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.UnsupportedOperatingSystem, "old"), PasskeyFlow.SignIn, configuration);
            PasskeyRecoveryPlan cancelled = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.UserCancelled, "cancelled"), PasskeyFlow.SignIn, configuration);

            PasskeyConfiguration noOffer = PasskeyClientFixture.ValidConfiguration();
            noOffer.Experience.OfferRegistration = false;
            PasskeyRecoveryPlan notOffered = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.NoCredentials, "none"), PasskeyFlow.SignIn, noOffer);

            PasskeyConfiguration noRegistration = PasskeyClientFixture.ValidConfiguration();
            noRegistration.Features.EnableRegistration = false;
            PasskeyRecoveryPlan registrationOff = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.NoCredentials, "none"), PasskeyFlow.SignIn, noRegistration);

            PasskeyConfiguration noAuthentication = PasskeyClientFixture.ValidConfiguration();
            noAuthentication.Features = new PasskeyFeatures { EnableAuthentication = false, EnableUsernameLessAuthentication = false };
            PasskeyRecoveryPlan authenticationOff = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.PlatformFailure, "broke"), PasskeyFlow.Registration, noAuthentication);

            return new AlternativesOutcome(
                Actions(network),
                Actions(unavailable),
                cancelled.Actions.Count,
                Actions(notOffered),
                Actions(registrationOff),
                Actions(authenticationOff),
                PasskeyRecovery.OffersRegistration(configuration),
                PasskeyRecovery.OffersRegistration(noOffer),
                PasskeyRecovery.OffersRegistration(noRegistration));
        })
        .TheTest
        .ShouldPass<AlternativesOutcome>((because, outcome) =>
        {
            because.ItsTrue("a network failure offers try again, then the other way: " + outcome.Network, outcome.Network == "TryAgain,UseAnotherWay");
            because.ItsTrue("an unsupported device still offers the other way: " + outcome.Unavailable, outcome.Unavailable == "UseAnotherWay");
            because.ItsTrue("a cancellation offers nothing, even with an alternative", outcome.CancelledActions == 0);
            because.ItsTrue("no create-account offer when the experience hides it: " + outcome.NotOffered, outcome.NotOffered == "TryAgain");
            because.ItsTrue("no create-account offer when registration is off: " + outcome.RegistrationOff, outcome.RegistrationOff == "TryAgain");
            because.ItsTrue("no sign-in offer when authentication is off: " + outcome.AuthenticationOff, outcome.AuthenticationOff == "TryAgain");
            because.ItsTrue("registration is offered by default", outcome.OffersByDefault);
            because.ItsTrue("registration is not offered when the experience hides it", !outcome.OffersWhenHidden);
            because.ItsTrue("registration is not offered when it is off", !outcome.OffersWhenOff);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void RefuseMissingArguments()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("plans with a null error and with a null configuration", (configuration, reg) =>
        {
            return new NullOutcome(
                ThrowsNull(() => PasskeyRecovery.For(null!, PasskeyFlow.SignIn, configuration)),
                ThrowsNull(() => PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.Unknown, "x"), PasskeyFlow.SignIn, null!)),
                ThrowsNull(() => PasskeyRecovery.OffersRegistration(null!)));
        })
        .TheTest
        .ShouldPass<NullOutcome>((because, outcome) =>
        {
            because.ItsTrue("a null error is refused", outcome.NullError);
            because.ItsTrue("a null configuration is refused", outcome.NullConfiguration);
            because.ItsTrue("OffersRegistration refuses a null configuration", outcome.NullOffers);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    // The table the screens are built to, written out independently of PasskeyRecovery's switch.
    private static string Expected(PasskeyErrorCategory category, PasskeyFlow flow, PasskeyText text)
    {
        string failed = flow == PasskeyFlow.SignIn ? text.SignInFailedTitle : text.RegistrationFailedTitle;
        string signInInstead = flow == PasskeyFlow.Registration ? ",SignInInstead" : string.Empty;
        string message = "said " + category;
        return category switch
        {
            PasskeyErrorCategory.UserCancelled => $"quiet|{string.Empty}|{text.Cancelled}|-|",
            PasskeyErrorCategory.UnsupportedOperatingSystem or PasskeyErrorCategory.FeatureDisabled => $"loud|{text.UnavailableTitle}|{message}|-|",
            PasskeyErrorCategory.MissingCredentialProvider or PasskeyErrorCategory.DeviceSecurityNotConfigured => $"loud|{text.DeviceSetupTitle}|{message}|{text.DeviceSetupHint}|TryAgain{signInInstead}",
            PasskeyErrorCategory.NetworkFailure => $"loud|{text.NetworkTitle}|{message}|-|TryAgain{signInInstead}",
            PasskeyErrorCategory.CredentialAlreadyRegistered => $"loud|{failed}|{message}|-|{signInInstead.TrimStart(',')}",
            PasskeyErrorCategory.NoCredentials when flow == PasskeyFlow.SignIn => $"loud|{failed}|{message}|-|TryAgain,CreateAccountInstead",
            _ => $"loud|{failed}|{message}|-|TryAgain{signInInstead}"
        };
    }

    private static string Describe(PasskeyRecoveryPlan plan)
    {
        return $"{(plan.IsQuiet ? "quiet" : "loud")}|{plan.Title}|{plan.Message}|{plan.Hint ?? "-"}|{Actions(plan)}";
    }

    private static string Actions(PasskeyRecoveryPlan plan)
    {
        return string.Join(",", plan.Actions);
    }

    private static bool ThrowsNull(Action call)
    {
        try
        {
            call();
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    private sealed record EveryCategoryOutcome(string Wrong);

    private sealed record AlternativesOutcome(string Network, string Unavailable, int CancelledActions, string NotOffered, string RegistrationOff, string AuthenticationOff, bool OffersByDefault, bool OffersWhenHidden, bool OffersWhenOff);

    private sealed record NullOutcome(bool NullError, bool NullConfiguration, bool NullOffers);
}
