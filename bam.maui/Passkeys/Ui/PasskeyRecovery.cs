namespace Bam.Maui.Passkeys;

/// <summary>Which flow a screen belongs to.</summary>
public enum PasskeyFlow
{
    /// <summary>Signing in to an existing account.</summary>
    SignIn = 0,

    /// <summary>Creating an account with its first passkey.</summary>
    Registration,

    /// <summary>Listing, adding, renaming and removing the signed-in account's passkeys, and signing out.</summary>
    Management,

    /// <summary>Saving the profile a sign-up couldn't write.</summary>
    Profile
}

/// <summary>A way out of a failed ceremony that an error screen offers.</summary>
public enum PasskeyRecoveryAction
{
    /// <summary>Run the same ceremony again.</summary>
    TryAgain = 0,

    /// <summary>The app's other way to sign in (<see cref="PasskeyExperience.AlternativeSignInLabel"/>).</summary>
    UseAnotherWay,

    /// <summary>From a failed registration to sign-in.</summary>
    SignInInstead,

    /// <summary>From a failed sign-in to registration.</summary>
    CreateAccountInstead,

    /// <summary>Back to the form, with what the user typed kept, to change it.  Every failure screen offers it.</summary>
    Back,

    /// <summary>The session has ended: to the sign-in screen.</summary>
    SignInAgain
}

/// <summary>
/// What a screen shows after a ceremony failed: a heading, the message, an optional hint, and the ways out.
/// A quiet plan (a dismissed prompt) is not an error: the screen stays ready and shows only the message.
/// </summary>
/// <param name="Title">The heading, or empty for a quiet plan.</param>
/// <param name="Message">The user-facing message.</param>
/// <param name="Hint">A second line telling the user what to do, or null.</param>
/// <param name="Actions">The ways out, in the order to show them.</param>
/// <param name="IsQuiet">True when the failure is a cancellation and must not read as an error.</param>
public sealed record PasskeyRecoveryPlan(string Title, string Message, string? Hint, IReadOnlyList<PasskeyRecoveryAction> Actions, bool IsQuiet);

/// <summary>
/// Maps a failed ceremony to what the screen offers next.  Pure: it reads only the error and the configuration,
/// so every category's screen is decided in one place and tested without a device.
/// </summary>
public static class PasskeyRecovery
{
    /// <summary>The plan for a failure.</summary>
    /// <param name="error">The normalized error.</param>
    /// <param name="flow">The flow that failed.</param>
    /// <param name="configuration">The app's configuration.</param>
    /// <param name="typedInput">
    /// Whether the ceremony used what the user typed (a username, the sign-up fields).  An error caused by that input
    /// isn't offered as "try again", which would send the same input; the user goes back to the form to change it.
    /// </param>
    /// <param name="replacesForm">
    /// Whether the failure came from the device check rather than a ceremony.  That screen takes the place of a form
    /// whose passkey buttons can't work, so it doesn't offer Back: going back would hide the reason and the re-check.
    /// </param>
    /// <returns>
    /// The plan.  Every plan that isn't quiet ends with <see cref="PasskeyRecoveryAction.Back"/> (except one that
    /// <paramref name="replacesForm"/>), so no failure screen is a dead end, and whenever the app has an alternative
    /// path, the plan offers it.
    /// </returns>
    public static PasskeyRecoveryPlan For(PasskeyError error, PasskeyFlow flow, PasskeyConfiguration configuration, bool typedInput = false, bool replacesForm = false)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(configuration);
        PasskeyText text = configuration.Text;
        string failedTitle = flow switch
        {
            PasskeyFlow.SignIn => text.SignInFailedTitle,
            PasskeyFlow.Management => text.ManagementFailedTitle,
            PasskeyFlow.Profile => text.ProfileFailedTitle,
            _ => text.RegistrationFailedTitle
        };
        List<PasskeyRecoveryAction> actions = new List<PasskeyRecoveryAction>();
        string title = failedTitle;
        string? hint = null;

        switch (error.Category)
        {
            case PasskeyErrorCategory.UserCancelled:
                // A dismissed prompt is the user's choice, not a failure: stay ready, say nothing alarming.
                return new PasskeyRecoveryPlan(string.Empty, text.Cancelled, null, Array.Empty<PasskeyRecoveryAction>(), true);
            case PasskeyErrorCategory.UnsupportedOperatingSystem:
            case PasskeyErrorCategory.FeatureDisabled:
                // Trying again can't help.
                title = text.UnavailableTitle;
                break;
            case PasskeyErrorCategory.MissingCredentialProvider:
            case PasskeyErrorCategory.DeviceSecurityNotConfigured:
                title = text.DeviceSetupTitle;
                hint = text.DeviceSetupHint;
                actions.Add(PasskeyRecoveryAction.TryAgain);
                break;
            case PasskeyErrorCategory.NetworkFailure:
                title = text.NetworkTitle;
                actions.Add(PasskeyRecoveryAction.TryAgain);
                break;
            case PasskeyErrorCategory.CredentialAlreadyRegistered:
                // This device already holds a passkey for the account: signing in is the way forward.
                break;
            case PasskeyErrorCategory.NotSignedIn when SignedInFlow(flow):
                // The session is gone: the same call fails the same way.  Signing in again is the way forward, or, when
                // this app doesn't sign in with passkeys, trying again once it has signed in another way.
                actions.Add(configuration.Features.EnableAuthentication ? PasskeyRecoveryAction.SignInAgain : PasskeyRecoveryAction.TryAgain);
                break;
            default:
                if (!(typedInput && CausedByInput(error.Category, flow)))
                {
                    actions.Add(PasskeyRecoveryAction.TryAgain);
                }
                break;
        }

        if (flow == PasskeyFlow.SignIn && error.Category == PasskeyErrorCategory.NoCredentials && OffersRegistration(configuration))
        {
            actions.Add(PasskeyRecoveryAction.CreateAccountInstead);
        }
        if (flow == PasskeyFlow.Registration && configuration.Features.EnableAuthentication && Recoverable(error.Category))
        {
            actions.Add(PasskeyRecoveryAction.SignInInstead);
        }
        if (configuration.Experience.AlternativeSignInLabel is not null)
        {
            actions.Add(PasskeyRecoveryAction.UseAnotherWay);
        }
        if (!replacesForm)
        {
            actions.Add(PasskeyRecoveryAction.Back);
        }
        return new PasskeyRecoveryPlan(title, error.Message, hint, actions, false);
    }

    /// <summary>Whether the sign-in screen offers to create an account.</summary>
    /// <param name="configuration">The app's configuration.</param>
    /// <returns>True when registration is on and the experience offers it.</returns>
    public static bool OffersRegistration(PasskeyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.Features.EnableRegistration && configuration.Experience.OfferRegistration;
    }

    // Errors the user's own input can cause: a refused username or sign-up field, a username with no passkey here, and
    // (registration) a sign-up the backend refused.  Sending the same input again can't help.
    private static bool CausedByInput(PasskeyErrorCategory category, PasskeyFlow flow)
    {
        return category == PasskeyErrorCategory.InvalidOptions
            || category == PasskeyErrorCategory.NoCredentials
            || (flow == PasskeyFlow.Registration && category == PasskeyErrorCategory.ServerVerificationFailed);
    }

    // The flows that need a session: management and the profile write.
    private static bool SignedInFlow(PasskeyFlow flow)
    {
        return flow == PasskeyFlow.Management || flow == PasskeyFlow.Profile;
    }

    // A device that can't do passkeys at all can't sign in with one either.
    private static bool Recoverable(PasskeyErrorCategory category)
    {
        return category != PasskeyErrorCategory.UnsupportedOperatingSystem && category != PasskeyErrorCategory.FeatureDisabled;
    }
}
