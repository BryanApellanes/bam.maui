namespace Bam.Maui.Passkeys;

/// <summary>
/// The app's navigation, called by the library's screens when the user leaves a flow.  The library assumes no
/// navigation framework: an app using Shell routes here, one using a NavigationPage pushes or pops, one that swaps
/// <c>Window.Page</c> does that.  Each call comes on the thread the screen's command ran on (the UI thread).
/// </summary>
public interface IPasskeyNavigator
{
    /// <summary>The account is signed in: the backend verified the ceremony.  Move to the signed-in part of the app.</summary>
    /// <param name="signIn">The signed-in account.</param>
    /// <param name="flow">Whether this came from sign-in or from creating the account.</param>
    /// <returns>A task that completes when navigation is done.</returns>
    Task SignedInAsync(PasskeySignIn signIn, PasskeyFlow flow);

    /// <summary>The user chose the app's other way to sign in (<see cref="PasskeyExperience.AlternativeSignInLabel"/>).</summary>
    /// <returns>A task that completes when navigation is done.</returns>
    Task AlternativeSignInRequestedAsync();

    /// <summary>The user wants to create an account.  Show the registration screen.</summary>
    /// <returns>A task that completes when navigation is done.</returns>
    Task RegistrationRequestedAsync();

    /// <summary>The user wants to sign in to an existing account.  Show the sign-in screen.</summary>
    /// <returns>A task that completes when navigation is done.</returns>
    Task SignInRequestedAsync();
}
