namespace Bam.Maui.Sample.WinUI;

/// <summary>The WinUI host for the sample app.</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Creates the WinUI application.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
