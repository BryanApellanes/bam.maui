namespace Bam.Maui.Passkeys;

/// <summary>
/// The registration screen as a reusable view: brand, heading, what a passkey is and why to use one, the sign-up
/// fields, the create button, and the branded success screen once the backend has verified the passkey.  Host it in
/// any page, or use <see cref="PasskeyRegistrationPage"/>.
/// </summary>
public sealed class PasskeyRegistrationView : PasskeyScreenLayout
{
    /// <summary>Creates the view.</summary>
    /// <param name="viewModel">The registration view model; it becomes the binding context.</param>
    /// <param name="theme">The app's passkey theme.</param>
    public PasskeyRegistrationView(PasskeyRegistrationViewModel viewModel, PasskeyTheme theme)
        : base(theme, (viewModel ?? throw new ArgumentNullException(nameof(viewModel))).Branding, Build(viewModel))
    {
        ViewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>The view model.</summary>
    public PasskeyRegistrationViewModel ViewModel { get; }

    private static View Build(PasskeyRegistrationViewModel viewModel)
    {
        PasskeyText text = viewModel.Text;
        VerticalStackLayout form = new VerticalStackLayout { Spacing = 16 };
        form.Children.Add(PasskeyViewParts.Heading(nameof(PasskeyRegistrationViewModel.Title)));
        form.Children.Add(PasskeyViewParts.Body(nameof(PasskeyRegistrationViewModel.Explanation)));
        if (viewModel.Benefits.Count > 0)
        {
            VerticalStackLayout benefits = new VerticalStackLayout { Spacing = 6 };
            foreach (string benefit in viewModel.Benefits)
            {
                // A bullet the screen reader skips: the line reads on its own.
                Label line = PasskeyViewParts.Fixed("•  " + benefit, 15, PasskeyResourceKeys.Text);
                SemanticProperties.SetDescription(line, benefit);
                benefits.Children.Add(line);
            }
            form.Children.Add(benefits);
        }

        Label note = PasskeyViewParts.Body(nameof(PasskeyFlowViewModel.Note), PasskeyResourceKeys.Text);
        PasskeyViewParts.ShowWhen(note, nameof(PasskeyFlowViewModel.HasNote));
        form.Children.Add(note);

        form.Children.Add(PasskeyViewParts.Field(text.UsernameLabel, nameof(PasskeyRegistrationViewModel.Handle), text.UsernamePlaceholder, Keyboard.Plain, false));
        form.Children.Add(PasskeyViewParts.Field(text.DisplayNameLabel, nameof(PasskeyRegistrationViewModel.DisplayName), string.Empty, Keyboard.Text, true));
        if (viewModel.AsksForEmail)
        {
            form.Children.Add(PasskeyViewParts.Field(text.EmailLabel, nameof(PasskeyRegistrationViewModel.Email), string.Empty, Keyboard.Email, false));
        }
        if (viewModel.AsksForInviteCode)
        {
            form.Children.Add(PasskeyViewParts.Field(text.InviteCodeLabel, nameof(PasskeyRegistrationViewModel.InviteCode), string.Empty, Keyboard.Plain, false));
        }
        form.Children.Add(PasskeyViewParts.Primary(text.CreatePasskey, nameof(PasskeyRegistrationViewModel.CreateCommand)));
        if (viewModel.HasAlternative)
        {
            form.Children.Add(PasskeyViewParts.Link(viewModel.AlternativeLabel, nameof(PasskeyFlowViewModel.UseAnotherWayCommand)));
        }
        if (viewModel.OffersSignIn)
        {
            form.Children.Add(PasskeyViewParts.Link(text.SignInPrompt, nameof(PasskeyFlowViewModel.GoToSignInCommand)));
        }
        PasskeyViewParts.ShowWhen(form, nameof(PasskeyFlowViewModel.IsReady));

        Label done = PasskeyViewParts.Heading(nameof(PasskeyRegistrationViewModel.SuccessTitle));

        // Sign-up claimed the account but couldn't write its profile: offer to save what was typed.
        VerticalStackLayout profile = new VerticalStackLayout { Spacing = 12 };
        profile.Children.Add(PasskeyViewParts.Body(nameof(PasskeyRegistrationViewModel.ProfileMessage), PasskeyResourceKeys.Text));
        profile.Children.Add(PasskeyViewParts.Field(text.DisplayNameLabel, nameof(PasskeyRegistrationViewModel.DisplayName), string.Empty, Keyboard.Text, true));
        if (viewModel.AsksForEmail)
        {
            profile.Children.Add(PasskeyViewParts.Field(text.EmailLabel, nameof(PasskeyRegistrationViewModel.Email), string.Empty, Keyboard.Email, false));
        }
        profile.Children.Add(PasskeyViewParts.Secondary(text.Save, nameof(PasskeyRegistrationViewModel.SaveProfileCommand)));
        PasskeyViewParts.ShowWhen(profile, nameof(PasskeyRegistrationViewModel.ShowsProfileStep));

        VerticalStackLayout success = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                done,
                PasskeyViewParts.Body(nameof(PasskeyRegistrationViewModel.SuccessDetail)),
                profile,
                PasskeyViewParts.Primary(text.Done, nameof(PasskeyRegistrationViewModel.DoneCommand))
            }
        };
        PasskeyViewParts.ShowWhen(success, nameof(PasskeyFlowViewModel.HasSucceeded));

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children = { form, success, new PasskeyStatusView(text, viewModel.Configuration.Experience.AlternativeSignInLabel) }
        };
    }
}
