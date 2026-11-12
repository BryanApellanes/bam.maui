namespace Bam.Maui.Passkeys;

/// <summary>
/// How the sign-in screen asks for an account.  A mode that needs username-less sign-in falls back to
/// <see cref="UsernameFirst"/> when <see cref="PasskeyFeatures.EnableUsernameLessAuthentication"/> is off
/// (see <see cref="PasskeyExperience.SignInModeFor"/>).
/// </summary>
public enum PasskeySignInMode
{
    /// <summary>
    /// One button. The operating system offers the passkeys it holds for the relying party, and no username is typed.
    /// Needs <see cref="PasskeyFeatures.EnableUsernameLessAuthentication"/>.
    /// </summary>
    UsernameLess = 0,

    /// <summary>The user enters their username first, then signs in with one of that account's passkeys.</summary>
    UsernameFirst,

    /// <summary>
    /// Both: a passkey button that needs no username, plus an optional username field for a user whose device
    /// doesn't offer the passkey on its own. Needs <see cref="PasskeyFeatures.EnableUsernameLessAuthentication"/>.
    /// </summary>
    Choice
}

/// <summary>
/// The shape of the flows around the native prompts: how sign-in asks for an account, and which other
/// paths the screens offer.
/// </summary>
public sealed class PasskeyExperience
{
    /// <summary>How the sign-in screen asks for an account. Default <see cref="PasskeySignInMode.Choice"/>.</summary>
    public PasskeySignInMode SignInMode { get; set; } = PasskeySignInMode.Choice;

    /// <summary>
    /// The label of the app's other way to sign in (for example "Sign in with email"), or null when there is none.
    /// When it is set, every sign-in and error screen offers that path, and choosing it calls
    /// <see cref="IPasskeyNavigator.AlternativeSignInRequestedAsync"/>.
    /// </summary>
    public string? AlternativeSignInLabel { get; set; }

    /// <summary>
    /// Whether the sign-in screen offers to create an account. Default true. It only shows when
    /// <see cref="PasskeyFeatures.EnableRegistration"/> is on.
    /// </summary>
    public bool OfferRegistration { get; set; } = true;

    /// <summary>Whether the registration screen asks for an email address. Default false.</summary>
    public bool AskForEmail { get; set; }

    /// <summary>Whether the registration screen asks for an invite code. Default false.</summary>
    public bool AskForInviteCode { get; set; }

    /// <summary>The mode the sign-in screen actually uses with these switches.</summary>
    /// <param name="features">The app's feature switches.</param>
    /// <returns><see cref="SignInMode"/>, or <see cref="PasskeySignInMode.UsernameFirst"/> when it needs username-less sign-in and that is off.</returns>
    public PasskeySignInMode SignInModeFor(PasskeyFeatures features)
    {
        ArgumentNullException.ThrowIfNull(features);
        return features.EnableUsernameLessAuthentication ? SignInMode : PasskeySignInMode.UsernameFirst;
    }

    /// <summary>Checks the experience is coherent.</summary>
    /// <param name="features">The app's feature switches.</param>
    /// <exception cref="ArgumentException">The sign-in mode is not a defined mode, or the alternative label is blank.</exception>
    public void Validate(PasskeyFeatures features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (!Enum.IsDefined(SignInMode))
        {
            throw new ArgumentException("PasskeyExperience.SignInMode is not a defined mode.");
        }
        if (AlternativeSignInLabel is not null && string.IsNullOrWhiteSpace(AlternativeSignInLabel))
        {
            throw new ArgumentException("PasskeyExperience.AlternativeSignInLabel must be null (no alternative) or a label to show.");
        }
    }
}
