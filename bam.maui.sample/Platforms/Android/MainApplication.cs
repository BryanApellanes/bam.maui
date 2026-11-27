using Android.App;
using Android.Runtime;

namespace Bam.Maui.Sample;

/// <summary>The sample's Android application.</summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>Creates the application; called by the Android runtime.</summary>
    /// <param name="handle">The Java object handle.</param>
    /// <param name="ownership">Who owns the handle.</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
