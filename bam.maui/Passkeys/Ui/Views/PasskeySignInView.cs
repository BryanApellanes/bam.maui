namespace Bam.Maui.Passkeys;

/// <summary>
/// The sign-in screen as a reusable view: brand, heading, welcome line, the passkey button and username field the
/// <see cref="PasskeySignInMode"/> calls for, the app's other way to sign in, and the link to registration.  Host it in
/// any page, or use <see cref="PasskeySignInPage"/>.  Call <see cref="PasskeySignInViewModel.LoadAsync"/> when it appears.
/// </summary>
public sealed class PasskeySignInView : PasskeyScreenLayout
{
    /// <summary>Creates the view.</summary>
    /// <param name="viewModel">The sign-in view model; it becomes the binding context.</param>
    /// <param name="theme">The app's passkey theme.</param>
    public PasskeySignInView(PasskeySignInViewModel viewModel, PasskeyTheme theme)
        : base(theme, (viewModel ?? throw new ArgumentNullException(nameof(viewModel))).Branding, Build(viewModel))
    {
        ViewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>The view model.</summary>
    public PasskeySignInViewModel ViewModel { get; }

    private static View Build(PasskeySignInViewModel viewModel)
    {
        PasskeyText text = viewModel.Text;
        VerticalStackLayout form = new VerticalStackLayout { Spacing = 16 };
        form.Children.Add(PasskeyViewParts.Heading(nameof(PasskeySignInViewModel.Title)));
        form.Children.Add(PasskeyViewParts.Body(nameof(PasskeySignInViewModel.Welcome)));

        Label note = PasskeyViewParts.Body(nameof(PasskeyFlowViewModel.Note), PasskeyResourceKeys.Text);
        PasskeyViewParts.ShowWhen(note, nameof(PasskeyFlowViewModel.HasNote));
        form.Children.Add(note);

        if (viewModel.ShowsPasskeyButton)
        {
            form.Children.Add(PasskeyViewParts.Primary(text.SignInWithPasskey, nameof(PasskeySignInViewModel.SignInCommand)));
        }
        if (viewModel.ShowsUsername)
        {
            if (viewModel.ShowsUsernameHint)
            {
                form.Children.Add(PasskeyViewParts.Fixed(text.UsernameChoiceHint, 14));
            }
            form.Children.Add(PasskeyViewParts.Field(text.UsernameLabel, nameof(PasskeySignInViewModel.Handle), text.UsernamePlaceholder, Keyboard.Plain, false));
            Button next = viewModel.ShowsPasskeyButton
                ? PasskeyViewParts.Secondary(text.Continue, nameof(PasskeySignInViewModel.ContinueCommand))
                : PasskeyViewParts.Primary(text.Continue, nameof(PasskeySignInViewModel.ContinueCommand));
            form.Children.Add(next);
        }
        if (viewModel.HasAlternative)
        {
            form.Children.Add(PasskeyViewParts.Link(viewModel.AlternativeLabel, nameof(PasskeyFlowViewModel.UseAnotherWayCommand)));
        }
        if (viewModel.OffersRegistration)
        {
            form.Children.Add(PasskeyViewParts.Link(text.CreateAccountPrompt, nameof(PasskeyFlowViewModel.GoToRegistrationCommand)));
        }
        PasskeyViewParts.ShowWhen(form, nameof(PasskeyFlowViewModel.IsReady));

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children = { form, new PasskeyStatusView(text, viewModel.Configuration.Experience.AlternativeSignInLabel) }
        };
    }
}
