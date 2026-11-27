namespace Bam.Maui.Sample;

/// <summary>The sample's application: one window, opening on the passkey sign-in screen.</summary>
public sealed class SampleApp : Microsoft.Maui.Controls.Application
{
    private readonly SampleNavigator _navigator;

    /// <summary>Creates the application.</summary>
    /// <param name="navigator">The sample's navigation.</param>
    public SampleApp(SampleNavigator navigator)
    {
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_navigator.StartPage()) { Title = "Passkey Sample" };
    }
}
