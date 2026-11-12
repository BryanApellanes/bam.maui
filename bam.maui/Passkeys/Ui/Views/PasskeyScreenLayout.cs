namespace Bam.Maui.Passkeys;

/// <summary>
/// The frame every passkey screen sits in: the brand (logo and app name) and the screen's content.  Narrow windows
/// (phones, folded foldables) stack them in one column capped at a readable width; from <see cref="WideWidth"/> up
/// (tablets, desktops, unfolded foldables) the brand moves to a column on the left.  Everything scrolls, so large
/// dynamic type never clips.
/// </summary>
public class PasskeyScreenLayout : ContentView
{
    /// <summary>The width from which the brand sits beside the content.</summary>
    public const double WideWidth = 840;

    /// <summary>The widest the content column grows.</summary>
    public const double ContentMaxWidth = 480;

    private readonly Grid _grid;
    private readonly View _brand;
    private readonly View _body;
    private bool? _wide;

    /// <summary>Creates the frame.</summary>
    /// <param name="theme">The app's passkey theme; its resources are merged here.</param>
    /// <param name="branding">The brand.</param>
    /// <param name="content">The screen's content.</param>
    public PasskeyScreenLayout(PasskeyTheme theme, PasskeyBranding branding, View content)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentNullException.ThrowIfNull(content);
        Theme = theme;
        Resources.MergedDictionaries.Add(theme.Resources);
        this.SetDynamicResource(BackgroundColorProperty, PasskeyResourceKeys.Background);

        _brand = Brand(branding);
        content.MaximumWidthRequest = ContentMaxWidth;
        content.HorizontalOptions = LayoutOptions.Fill;
        _body = new ContentView { Content = content, HorizontalOptions = LayoutOptions.Center, WidthRequest = -1 };
        _grid = new Grid { Padding = new Thickness(24, 32), ColumnSpacing = 48, RowSpacing = 24 };
        Content = new ScrollView { Content = _grid };
        Arrange(false);
        SizeChanged += (sender, args) => Arrange(Width >= WideWidth);
        Loaded += (sender, args) => Theme.Follow(Microsoft.Maui.Controls.Application.Current);
    }

    /// <summary>The theme the frame was built with.</summary>
    public PasskeyTheme Theme { get; }

    /// <summary>Whether the brand currently sits beside the content.</summary>
    public bool IsWide => _wide == true;

    /// <summary>Lays the frame out for a narrow or a wide window.</summary>
    /// <param name="wide">Whether the window is at least <see cref="WideWidth"/> wide.</param>
    public void Arrange(bool wide)
    {
        if (_wide == wide)
        {
            return;
        }
        _wide = wide;
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        if (wide)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _brand.VerticalOptions = LayoutOptions.Center;
            _body.VerticalOptions = LayoutOptions.Center;
            _grid.Add(_brand, 0, 0);
            _grid.Add(_body, 1, 0);
        }
        else
        {
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            _brand.VerticalOptions = LayoutOptions.Start;
            _body.VerticalOptions = LayoutOptions.Start;
            _grid.Add(_brand, 0, 0);
            _grid.Add(_body, 0, 1);
        }
    }

    private static View Brand(PasskeyBranding branding)
    {
        VerticalStackLayout brand = new VerticalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center };
        if (!string.IsNullOrWhiteSpace(branding.Logo))
        {
            Image logo = new Image { Source = ImageSource.FromFile(branding.Logo), HeightRequest = 72, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center };
            SemanticProperties.SetDescription(logo, branding.ApplicationName);
            brand.Children.Add(logo);
        }
        Label name = PasskeyViewParts.Fixed(branding.ApplicationName, 20, PasskeyResourceKeys.Text);
        name.FontAttributes = FontAttributes.Bold;
        name.HorizontalTextAlignment = TextAlignment.Center;
        brand.Children.Add(name);
        return brand;
    }
}
