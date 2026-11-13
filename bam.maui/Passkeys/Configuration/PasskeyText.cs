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

    /// <summary>The failure screen's way back to the form.</summary>
    public string Back { get; set; } = "Back";

    /// <summary>The recovery note telling the user what to set up, for a device that needs it.</summary>
    public string DeviceSetupHint { get; set; } = "Open your device settings, then come back and try again.";

    /// <summary>The recovery button from a failed registration to sign-in.</summary>
    public string SignInInstead { get; set; } = "Sign in instead";

    /// <summary>The recovery button from a failed sign-in to registration.</summary>
    public string CreateAccountInstead { get; set; } = "Create an account";

    /// <summary>The recovery button when the session has ended.</summary>
    public string SignInAgain { get; set; } = "Sign in again";

    /// <summary>The progress line for a call that puts up no system prompt (listing, renaming, removing, signing out, saving the profile).</summary>
    public string Updating { get; set; } = "Just a moment.";

    /// <summary>The management heading.</summary>
    public string ManagementTitle { get; set; } = "Your passkeys";

    /// <summary>The line under the management heading. Takes the app name.</summary>
    public string ManagementIntro { get; set; } = "These are the passkeys you can sign in to {0} with.";

    /// <summary>The heading of the empty state, when the account has no passkeys.</summary>
    public string NoPasskeysTitle { get; set; } = "No passkeys yet";

    /// <summary>The empty state's explanation.</summary>
    public string NoPasskeysMessage { get; set; } = "Add a passkey to sign in with this device instead of a password.";

    /// <summary>The button that adds another passkey.</summary>
    public string AddPasskey { get; set; } = "Add a passkey";

    /// <summary>The label of the field naming a passkey, when adding or renaming one.</summary>
    public string PasskeyNameLabel { get; set; } = "Passkey name (optional)";

    /// <summary>The hint inside an empty passkey name field.</summary>
    public string PasskeyNamePlaceholder { get; set; } = "For example, My phone";

    /// <summary>What a passkey without a name is called in the list.</summary>
    public string UnnamedPasskey { get; set; } = "Passkey";

    /// <summary>When a passkey was added. <c>{0}</c> is the date.</summary>
    public string AddedOn { get; set; } = "Added {0}";

    /// <summary>When a passkey last signed in. <c>{0}</c> is the date.</summary>
    public string LastUsedOn { get; set; } = "Last used {0}";

    /// <summary>The tag on the passkey this session was opened with.</summary>
    public string CurrentPasskey { get; set; } = "You signed in with this one";

    /// <summary>The button that renames a passkey.</summary>
    public string Rename { get; set; } = "Rename";

    /// <summary>The button that saves a passkey's new name, or a profile.</summary>
    public string Save { get; set; } = "Save";

    /// <summary>The button that removes a passkey.</summary>
    public string Remove { get; set; } = "Remove";

    /// <summary>The heading of the removal confirmation.</summary>
    public string RemoveTitle { get; set; } = "Remove this passkey?";

    /// <summary>What removing a passkey does.</summary>
    public string RemoveMessage { get; set; } = "You won't be able to sign in with it any more. Your password manager may keep it until you delete it there.";

    /// <summary>The extra line when the passkey is the one this session was opened with.</summary>
    public string RemoveCurrentMessage { get; set; } = "You signed in with this passkey, so removing it signs you out here.";

    /// <summary>The button that keeps the passkey and closes the removal confirmation.</summary>
    public string Keep { get; set; } = "Keep it";

    /// <summary>Shown when the server refuses to remove the account's only passkey.</summary>
    public string LastPasskeyMessage { get; set; } = "This is your only passkey. Add another one before you remove it.";

    /// <summary>The tag on the passkey that holds the account key (the account's registered key set).</summary>
    public string AccountKeyPasskey { get; set; } = "Account key";

    /// <summary>The removal confirmation's extra line for the passkey that holds the account key.</summary>
    public string RemoveAccountKeyMessage { get; set; } = "This passkey holds your account key.";

    /// <summary>The heading of the panel that moves the account key before its passkey is removed.</summary>
    public string MoveAccountKeyTitle { get; set; } = "Move your account key";

    /// <summary>What moving the account key does.</summary>
    public string MoveAccountKeyMessage { get; set; } = "Choose another of your passkeys to hold your account key. Then confirm with the passkey you're removing.";

    /// <summary>The label of the choice of passkey to move the account key to.</summary>
    public string MoveAccountKeyTo { get; set; } = "Move it to";

    /// <summary>The button that moves the account key and removes the passkey.</summary>
    public string MoveAccountKey { get; set; } = "Move key and remove";

    /// <summary>The button that removes the passkey without moving the account key.</summary>
    public string RemoveWithoutMoving { get; set; } = "Remove without moving";

    /// <summary>What removing without moving does, shown beside <see cref="RemoveWithoutMoving"/>.</summary>
    public string RemoveWithoutMovingMessage { get; set; } = "If you no longer have the device, remove it without moving. You can't sign in with it after that, but its key still counts as your account key until it is replaced, and you can't replace it here yet.";

    /// <summary>Shown when the server refuses the passkey chosen to hold the account key.</summary>
    public string AccountKeyHeirRefusedMessage { get; set; } = "That passkey can't hold your account key. Choose another one.";

    /// <summary>Shown when the account's passkeys changed while the account key was being moved; Back loads the list again.</summary>
    public string PasskeysChangedMessage { get; set; } = "Your passkeys changed while this was open. Check the list and try again.";

    /// <summary>The button that loads the list again.</summary>
    public string Refresh { get; set; } = "Refresh";

    /// <summary>The button that ends the session.</summary>
    public string SignOut { get; set; } = "Sign out";

    /// <summary>The heading of an error screen when listing or changing passkeys failed.</summary>
    public string ManagementFailedTitle { get; set; } = "Couldn't update your passkeys";

    /// <summary>The heading of an error screen when saving the profile failed.</summary>
    public string ProfileFailedTitle { get; set; } = "Couldn't save your details";

    /// <summary>Shown on the registration success screen when sign-up couldn't save the name and email.</summary>
    public string ProfileNotSavedMessage { get; set; } = "Your account is ready, but your name and email weren't saved. Save them now?";

    /// <summary>Shown on the sign-in screen while the app takes over after sign-in.</summary>
    public string SigningIn { get; set; } = "Opening your account.";

    /// <summary>The heading on the management screen while the app takes over after sign-out.</summary>
    public string SignedOutTitle { get; set; } = "You're signed out";

    /// <summary>Shown under <see cref="SignedOutTitle"/>.</summary>
    public string SigningOut { get; set; } = "Closing your account.";

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
