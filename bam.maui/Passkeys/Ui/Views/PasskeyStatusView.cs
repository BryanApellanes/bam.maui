namespace Bam.Maui.Passkeys;

/// <summary>
/// The progress and error parts of a passkey screen, bound to a <see cref="PasskeyFlowViewModel"/>: a spinner and a
/// cancel button while the system prompt is up, and after a failure the heading, the message, the hint and one button
/// per recovery action.  A dismissed prompt never shows here; it is a note on the form.
/// </summary>
public sealed class PasskeyStatusView : ContentView
{
    /// <summary>Creates the view.  Its <c>BindingContext</c> is inherited from the screen.</summary>
    /// <param name="text">The on-screen text.</param>
    /// <param name="alternativeLabel">The label of the app's other way to sign in, or null.</param>
    public PasskeyStatusView(PasskeyText text, string? alternativeLabel)
    {
        ArgumentNullException.ThrowIfNull(text);

        ActivityIndicator spinner = new ActivityIndicator { IsRunning = true, HeightRequest = 32 };
        spinner.SetDynamicResource(ActivityIndicator.ColorProperty, PasskeyResourceKeys.Primary);
        Label working = PasskeyViewParts.Body(nameof(PasskeyFlowViewModel.WorkingText), PasskeyResourceKeys.Text);
        working.HorizontalTextAlignment = TextAlignment.Center;
        SemanticProperties.SetDescription(working, text.Working);
        VerticalStackLayout progress = new VerticalStackLayout
        {
            Spacing = 16,
            Children = { spinner, working, PasskeyViewParts.Link(text.Cancel, nameof(PasskeyFlowViewModel.CancelCommand)) }
        };
        PasskeyViewParts.ShowWhen(progress, nameof(PasskeyFlowViewModel.IsWorking));

        Label title = PasskeyViewParts.Heading(nameof(PasskeyFlowViewModel.ErrorTitle));
        title.SetDynamicResource(Label.TextColorProperty, PasskeyResourceKeys.Error);
        Label message = PasskeyViewParts.Body(nameof(PasskeyFlowViewModel.ErrorMessage), PasskeyResourceKeys.Text);
        Label hint = PasskeyViewParts.Body(nameof(PasskeyFlowViewModel.ErrorHint));
        PasskeyViewParts.ShowWhen(hint, nameof(PasskeyFlowViewModel.HasErrorHint));
        Button tryAgain = PasskeyViewParts.Primary(text.TryAgain, nameof(PasskeyFlowViewModel.TryAgainCommand));
        PasskeyViewParts.ShowWhen(tryAgain, nameof(PasskeyFlowViewModel.OffersTryAgain));
        Button signIn = PasskeyViewParts.Secondary(text.SignInInstead, nameof(PasskeyFlowViewModel.GoToSignInCommand));
        PasskeyViewParts.ShowWhen(signIn, nameof(PasskeyFlowViewModel.OffersSignInInstead));
        Button register = PasskeyViewParts.Secondary(text.CreateAccountInstead, nameof(PasskeyFlowViewModel.GoToRegistrationCommand));
        PasskeyViewParts.ShowWhen(register, nameof(PasskeyFlowViewModel.OffersCreateAccountInstead));
        VerticalStackLayout failure = new VerticalStackLayout { Spacing = 16, Children = { title, message, hint, tryAgain, signIn, register } };
        if (alternativeLabel is not null)
        {
            Button another = PasskeyViewParts.Link(alternativeLabel, nameof(PasskeyFlowViewModel.UseAnotherWayCommand));
            PasskeyViewParts.ShowWhen(another, nameof(PasskeyFlowViewModel.OffersAnotherWay));
            failure.Children.Add(another);
        }
        PasskeyViewParts.ShowWhen(failure, nameof(PasskeyFlowViewModel.HasFailed));

        Content = new VerticalStackLayout { Spacing = 0, Children = { progress, failure } };
    }
}
