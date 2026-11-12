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
            because.ItsTrue("a network failure offers try again, then the other way, then back: " + outcome.Network, outcome.Network == "TryAgain,UseAnotherWay,Back");
            because.ItsTrue("an unsupported device still offers the other way: " + outcome.Unavailable, outcome.Unavailable == "UseAnotherWay,Back");
            because.ItsTrue("a cancellation offers nothing, even with an alternative", outcome.CancelledActions == 0);
            because.ItsTrue("no create-account offer when the experience hides it: " + outcome.NotOffered, outcome.NotOffered == "TryAgain,Back");
            because.ItsTrue("no create-account offer when registration is off: " + outcome.RegistrationOff, outcome.RegistrationOff == "TryAgain,Back");
            because.ItsTrue("no sign-in offer when authentication is off: " + outcome.AuthenticationOff, outcome.AuthenticationOff == "TryAgain,Back");
            because.ItsTrue("registration is offered by default", outcome.OffersByDefault);
            because.ItsTrue("registration is not offered when the experience hides it", !outcome.OffersWhenHidden);
            because.ItsTrue("registration is not offered when it is off", !outcome.OffersWhenOff);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NeverLeaveADeadEnd()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("plans every category, flow, typed input and alternative, with and without registration", (configuration, reg) =>
        {
            List<string> deadEnds = new List<string>();
            PasskeyConfiguration withAlternative = PasskeyClientFixture.ValidConfiguration();
            withAlternative.Experience.AlternativeSignInLabel = "Email me a link";
            PasskeyConfiguration bare = PasskeyClientFixture.ValidConfiguration();
            bare.Features.EnableRegistration = false;
            foreach (PasskeyConfiguration candidate in new[] { configuration, withAlternative, bare })
            {
                foreach (PasskeyErrorCategory category in Enum.GetValues<PasskeyErrorCategory>())
                {
                    foreach (PasskeyFlow flow in Enum.GetValues<PasskeyFlow>())
                    {
                        foreach (bool typed in new[] { false, true })
                        {
                            PasskeyRecoveryPlan plan = PasskeyRecovery.For(new PasskeyError(category, "x"), flow, candidate, typed);
                            if (!plan.IsQuiet && (plan.Actions.Count == 0 || plan.Actions[plan.Actions.Count - 1] != PasskeyRecoveryAction.Back))
                            {
                                deadEnds.Add($"{flow}/{category}/typed={typed}: [{Actions(plan)}]");
                            }
                        }
                        // The device check's screen replaces a form that can't be used: no Back, but it always says why.
                        PasskeyRecoveryPlan check = PasskeyRecovery.For(new PasskeyError(category, "x"), flow, candidate, false, true);
                        if (!check.IsQuiet && (check.Actions.Contains(PasskeyRecoveryAction.Back) || check.Title.Length == 0 || check.Message.Length == 0))
                        {
                            deadEnds.Add($"{flow}/{category}/check: [{Actions(check)}] '{check.Title}'");
                        }
                    }
                }
            }
            return new DeadEndOutcome(string.Join("; ", deadEnds));
        })
        .TheTest
        .ShouldPass<DeadEndOutcome>((because, outcome) =>
        {
            because.ItsTrue("every failure screen ends with back, and the device check's explains itself without one: " + outcome.DeadEnds, outcome.DeadEnds.Length == 0);
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void NotReplayInputTheUserMustChange()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("plans input-shaped and other failures with and without typed input", (configuration, reg) =>
        {
            return new TypedInputOutcome(
                Plan(PasskeyErrorCategory.InvalidOptions, PasskeyFlow.Registration, configuration, true),
                Plan(PasskeyErrorCategory.ServerVerificationFailed, PasskeyFlow.Registration, configuration, true),
                Plan(PasskeyErrorCategory.ServerVerificationFailed, PasskeyFlow.SignIn, configuration, true),
                Plan(PasskeyErrorCategory.NoCredentials, PasskeyFlow.SignIn, configuration, true),
                Plan(PasskeyErrorCategory.InvalidOptions, PasskeyFlow.SignIn, configuration, true),
                Plan(PasskeyErrorCategory.NetworkFailure, PasskeyFlow.Registration, configuration, true),
                Plan(PasskeyErrorCategory.InvalidOptions, PasskeyFlow.Registration, configuration, false));
        })
        .TheTest
        .ShouldPass<TypedInputOutcome>((because, outcome) =>
        {
            because.ItsTrue("a refused sign-up field goes back to the form: " + outcome.RegistrationRefusedField, outcome.RegistrationRefusedField == "SignInInstead,Back");
            because.ItsTrue("a refused sign-up goes back to the form: " + outcome.RegistrationRefused, outcome.RegistrationRefused == "SignInInstead,Back");
            because.ItsTrue("a failed sign-in verification can still be tried again: " + outcome.SignInVerification, outcome.SignInVerification == "TryAgain,Back");
            because.ItsTrue("a typed username with no passkey goes back to the form: " + outcome.TypedNoCredentials, outcome.TypedNoCredentials == "CreateAccountInstead,Back");
            because.ItsTrue("a refused typed username goes back to the form: " + outcome.TypedRefused, outcome.TypedRefused == "Back");
            because.ItsTrue("a network failure is still tried again with typed input: " + outcome.TypedNetwork, outcome.TypedNetwork == "TryAgain,SignInInstead,Back");
            because.ItsTrue("without typed input, try again stays: " + outcome.UntypedRefused, outcome.UntypedRefused == "TryAgain,SignInInstead,Back");
        })
        .SoBeHappy()
        .UnlessItFailed();
    }

    [UnitTest]
    public void SendAnEndedSessionToSignIn()
    {
        After.Setup(reg => reg.Set(PasskeyClientFixture.ValidConfiguration()))
        .When<PasskeyConfiguration>("plans a lost session in each flow, with passkey sign-in on and off, and in place of the list", (configuration, reg) =>
        {
            PasskeyConfiguration noSignIn = PasskeyClientFixture.ValidConfiguration();
            noSignIn.Features.EnableAuthentication = false;
            PasskeyRecoveryPlan list = PasskeyRecovery.For(new PasskeyError(PasskeyErrorCategory.NotSignedIn, "x"), PasskeyFlow.Management, configuration, false, true);
            return new EndedSessionOutcome(
                Plan(PasskeyErrorCategory.NotSignedIn, PasskeyFlow.Management, configuration, false),
                Plan(PasskeyErrorCategory.NotSignedIn, PasskeyFlow.Profile, configuration, true),
                Plan(PasskeyErrorCategory.NotSignedIn, PasskeyFlow.Management, noSignIn, false),
                Plan(PasskeyErrorCategory.NotSignedIn, PasskeyFlow.SignIn, configuration, false),
                Actions(list),
                Plan(PasskeyErrorCategory.InvalidOptions, PasskeyFlow.Management, configuration, true),
                Plan(PasskeyErrorCategory.InvalidOptions, PasskeyFlow.Profile, configuration, true));
        })
        .TheTest
        .ShouldPass<EndedSessionOutcome>((because, outcome) =>
        {
            because.ItsTrue("a lost session while managing offers sign in again: " + outcome.Management, outcome.Management == "SignInAgain,Back");
            because.ItsTrue("so does one while saving the profile: " + outcome.Profile, outcome.Profile == "SignInAgain,Back");
            because.ItsTrue("an app without passkey sign-in offers try again instead: " + outcome.WithoutSignIn, outcome.WithoutSignIn == "TryAgain,Back");
            because.ItsTrue("sign-in itself is unchanged: " + outcome.SignIn, outcome.SignIn == "TryAgain,Back");
            because.ItsTrue("a list that couldn't load for a lost session still has a way out: " + outcome.InPlaceOfList, outcome.InPlaceOfList == "SignInAgain");
            because.ItsTrue("a refused rename or removal goes back, not again: " + outcome.RefusedChange, outcome.RefusedChange == "Back");
            because.ItsTrue("a refused profile goes back, not again: " + outcome.RefusedProfile, outcome.RefusedProfile == "Back");
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
        string failed = flow switch
        {
            PasskeyFlow.SignIn => text.SignInFailedTitle,
            PasskeyFlow.Management => text.ManagementFailedTitle,
            PasskeyFlow.Profile => text.ProfileFailedTitle,
            _ => text.RegistrationFailedTitle
        };
        string signInInstead = flow == PasskeyFlow.Registration ? ",SignInInstead" : string.Empty;
        bool signedIn = flow == PasskeyFlow.Management || flow == PasskeyFlow.Profile;
        string message = "said " + category;
        return category switch
        {
            PasskeyErrorCategory.NotSignedIn when signedIn => $"loud|{failed}|{message}|-|SignInAgain,Back",
            PasskeyErrorCategory.UserCancelled => $"quiet|{string.Empty}|{text.Cancelled}|-|",
            PasskeyErrorCategory.UnsupportedOperatingSystem or PasskeyErrorCategory.FeatureDisabled => $"loud|{text.UnavailableTitle}|{message}|-|Back",
            PasskeyErrorCategory.MissingCredentialProvider or PasskeyErrorCategory.DeviceSecurityNotConfigured => $"loud|{text.DeviceSetupTitle}|{message}|{text.DeviceSetupHint}|TryAgain{signInInstead},Back",
            PasskeyErrorCategory.NetworkFailure => $"loud|{text.NetworkTitle}|{message}|-|TryAgain{signInInstead},Back",
            PasskeyErrorCategory.CredentialAlreadyRegistered => $"loud|{failed}|{message}|-|{signInInstead.TrimStart(',')}{(signInInstead.Length > 0 ? "," : string.Empty)}Back",
            PasskeyErrorCategory.NoCredentials when flow == PasskeyFlow.SignIn => $"loud|{failed}|{message}|-|TryAgain,CreateAccountInstead,Back",
            _ => $"loud|{failed}|{message}|-|TryAgain{signInInstead},Back"
        };
    }

    private static string Plan(PasskeyErrorCategory category, PasskeyFlow flow, PasskeyConfiguration configuration, bool typed)
    {
        return Actions(PasskeyRecovery.For(new PasskeyError(category, "x"), flow, configuration, typed));
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

    private sealed record DeadEndOutcome(string DeadEnds);

    private sealed record TypedInputOutcome(string RegistrationRefusedField, string RegistrationRefused, string SignInVerification, string TypedNoCredentials, string TypedRefused,
        string TypedNetwork, string UntypedRefused);

    private sealed record NullOutcome(bool NullError, bool NullConfiguration, bool NullOffers);

    private sealed record EndedSessionOutcome(string Management, string Profile, string WithoutSignIn, string SignIn, string InPlaceOfList, string RefusedChange, string RefusedProfile);
}
