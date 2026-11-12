using System.Reflection;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Every piece of text the library's screens show apart from error messages (those are <see cref="PasskeyMessages"/>).
/// Replace members to reword or localize. <c>{0}</c> in a member marked "takes the app name" is replaced with
/// <see cref="PasskeyBranding.ApplicationName"/>.
/// </summary>
public sealed class PasskeyText
{
    /// <summary>The sign-in heading. Takes the app name.</summary>
    public string SignInTitle { get; set; } = "Sign in to {0}";

    /// <summary>The line under the sign-in heading.</summary>
    public string Welcome { get; set; } = "Welcome back. Use the passkey saved on this device.";

    /// <summary>The main sign-in button.</summary>
    public string SignInWithPasskey { get; set; } = "Sign in with a passkey";

    /// <summary>The label of the username field.</summary>
    public string UsernameLabel { get; set; } = "Username";

    /// <summary>The hint inside an empty username field.</summary>
    public string UsernamePlaceholder { get; set; } = "Your username";

    /// <summary>The note above the username field in <see cref="PasskeySignInMode.Choice"/> mode.</summary>
    public string UsernameChoiceHint { get; set; } = "Passkey not offered? Enter your username.";

    /// <summary>The button that signs in the entered username.</summary>
    public string Continue { get; set; } = "Continue";

    /// <summary>The sign-in screen's link to registration.</summary>
    public string CreateAccountPrompt { get; set; } = "New here? Create an account";

    /// <summary>The registration heading. Takes the app name.</summary>
    public string RegistrationTitle { get; set; } = "Create your {0} account";

    /// <summary>What a passkey is, shown on the registration screen.</summary>
    public string PasskeyExplanation { get; set; } = "A passkey replaces your password. It's saved on this device, and you unlock it the same way you unlock the device.";

    /// <summary>The benefits listed on the registration screen, one line each.</summary>
    public IReadOnlyList<string> PasskeyBenefits { get; set; } = new[]
    {
        "Nothing to remember or type.",
        "It can't be phished or reused on another site.",
        "It syncs to your other devices through your password manager."
    };

    /// <summary>The label of the display name field.</summary>
    public string DisplayNameLabel { get; set; } = "Name";

    /// <summary>The label of the email field.</summary>
    public string EmailLabel { get; set; } = "Email";

    /// <summary>The label of the invite code field.</summary>
    public string InviteCodeLabel { get; set; } = "Invite code";

    /// <summary>The button that creates the account and its passkey.</summary>
    public string CreatePasskey { get; set; } = "Create a passkey";

    /// <summary>The registration screen's link back to sign-in.</summary>
    public string SignInPrompt { get; set; } = "Already have an account? Sign in";

    /// <summary>Shown while a ceremony runs and the system prompt is up.</summary>
    public string Working { get; set; } = "Follow the prompt on your device.";

    /// <summary>The button that stops a ceremony in progress.</summary>
    public string Cancel { get; set; } = "Cancel";

    /// <summary>The success heading after signing in. Takes the app name.</summary>
    public string SignedInTitle { get; set; } = "You're signed in to {0}";

    /// <summary>The success heading after creating an account. Takes the app name.</summary>
    public string RegisteredTitle { get; set; } = "Your {0} account is ready";

    /// <summary>The line under a registration success heading.</summary>
    public string RegisteredDetail { get; set; } = "Your passkey is saved. Next time, sign in with it.";

    /// <summary>The button that leaves a success screen.</summary>
    public string Done { get; set; } = "Continue";

    /// <summary>The heading of an error screen when sign-in failed.</summary>
    public string SignInFailedTitle { get; set; } = "Couldn't sign in";

    /// <summary>The heading of an error screen when registration failed.</summary>
    public string RegistrationFailedTitle { get; set; } = "Couldn't create the passkey";

    /// <summary>The heading of an error screen when the device can't use passkeys.</summary>
    public string UnavailableTitle { get; set; } = "Passkeys aren't available";

    /// <summary>The heading of an error screen when the device needs setting up first.</summary>
    public string DeviceSetupTitle { get; set; } = "This device needs setting up";

    /// <summary>The heading of an error screen when the network failed.</summary>
    public string NetworkTitle { get; set; } = "You're offline";

    /// <summary>The note shown after the user dismissed the system prompt. Not an error.</summary>
    public string Cancelled { get; set; } = "No problem. Try again whenever you're ready.";

    /// <summary>The recovery button that runs the ceremony again.</summary>
    public string TryAgain { get; set; } = "Try again";

    /// <summary>The recovery note telling the user what to set up, for a device that needs it.</summary>
    public string DeviceSetupHint { get; set; } = "Open your device settings, then come back and try again.";

    /// <summary>The recovery button from a failed registration to sign-in.</summary>
    public string SignInInstead { get; set; } = "Sign in instead";

    /// <summary>The recovery button from a failed sign-in to registration.</summary>
    public string CreateAccountInstead { get; set; } = "Create an account";

    /// <summary>Puts the app name into a member that takes it.</summary>
    /// <param name="template">The member's text.</param>
    /// <param name="applicationName">The brand's app name.</param>
    /// <returns>The text with <c>{0}</c> replaced; any other brace is left as it is.</returns>
    public static string WithName(string template, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(template);
        // Not string.Format: a translation with a stray brace must not throw on screen.
        return template.Replace("{0}", applicationName ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Checks every member has text.</summary>
    /// <exception cref="ArgumentException">A member is null or blank; the message names it.</exception>
    public void Validate()
    {
        foreach (PropertyInfo property in typeof(PasskeyText).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(string))
            {
                if (string.IsNullOrWhiteSpace((string?)property.GetValue(this)))
                {
                    throw new ArgumentException($"PasskeyText.{property.Name} must have text.");
                }
            }
        }
        if (PasskeyBenefits is null || PasskeyBenefits.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("PasskeyText.PasskeyBenefits must be a list of non-blank lines (it may be empty).");
        }
    }
}
