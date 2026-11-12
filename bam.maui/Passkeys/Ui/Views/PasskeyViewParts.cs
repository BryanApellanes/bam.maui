namespace Bam.Maui.Passkeys;

/// <summary>
/// The controls the screens are built from.  Every colour, radius and font comes from <see cref="PasskeyResourceKeys"/>
/// through <c>DynamicResource</c>, so nothing brand-specific is fixed here.  Text is bound or passed from
/// <see cref="PasskeyText"/>; font auto-scaling stays on so dynamic type works, and touch targets are at least 44 high.
/// </summary>
internal static class PasskeyViewParts
{
    internal const double MinimumTouchTarget = 44;

    internal static Label Heading(string textPath)
    {
        Label label = new Label { FontSize = 26, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.WordWrap };
        label.SetBinding(Label.TextProperty, textPath);
        SemanticProperties.SetHeadingLevel(label, SemanticHeadingLevel.Level1);
        Themed(label, PasskeyResourceKeys.Text);
        return label;
    }

    internal static Label Body(string textPath, string colorKey = PasskeyResourceKeys.MutedText)
    {
        Label label = new Label { FontSize = 16, LineBreakMode = LineBreakMode.WordWrap };
        label.SetBinding(Label.TextProperty, textPath);
        Themed(label, colorKey);
        return label;
    }

    internal static Label Fixed(string text, double fontSize = 16, string colorKey = PasskeyResourceKeys.MutedText)
    {
        Label label = new Label { Text = text, FontSize = fontSize, LineBreakMode = LineBreakMode.WordWrap };
        Themed(label, colorKey);
        return label;
    }

    internal static Button Primary(string text, string commandPath)
    {
        Button button = Button(text, commandPath);
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, PasskeyResourceKeys.Primary);
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, PasskeyResourceKeys.OnPrimary);
        button.FontAttributes = FontAttributes.Bold;
        return button;
    }

    internal static Button Secondary(string text, string commandPath)
    {
        Button button = Button(text, commandPath);
        button.BackgroundColor = Colors.Transparent;
        button.BorderWidth = 1.5;
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.BorderColorProperty, PasskeyResourceKeys.Primary);
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, PasskeyResourceKeys.Primary);
        return button;
    }

    internal static Button Link(string text, string commandPath)
    {
        Button button = Button(text, commandPath);
        button.BackgroundColor = Colors.Transparent;
        button.BorderWidth = 0;
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, PasskeyResourceKeys.Secondary);
        return button;
    }

    internal static VerticalStackLayout Field(string label, string textPath, string placeholder, Keyboard keyboard, bool autoComplete)
    {
        Label caption = Fixed(label, 14, PasskeyResourceKeys.Text);
        Entry entry = new Entry
        {
            Placeholder = placeholder,
            Keyboard = keyboard,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = autoComplete,
            MinimumHeightRequest = MinimumTouchTarget
        };
        entry.SetBinding(Entry.TextProperty, textPath, BindingMode.TwoWay);
        entry.SetDynamicResource(Entry.TextColorProperty, PasskeyResourceKeys.Text);
        entry.SetDynamicResource(Entry.PlaceholderColorProperty, PasskeyResourceKeys.MutedText);
        entry.SetDynamicResource(Entry.FontFamilyProperty, PasskeyResourceKeys.FontFamily);
        SemanticProperties.SetDescription(entry, label);
        return new VerticalStackLayout { Spacing = 4, Children = { caption, entry } };
    }

    internal static void ShowWhen(VisualElement element, string boolPath)
    {
        element.SetBinding(VisualElement.IsVisibleProperty, boolPath);
    }

    private static Button Button(string text, string commandPath)
    {
        Button button = new Button { Text = text, MinimumHeightRequest = MinimumTouchTarget, Padding = new Thickness(20, 12), FontSize = 16, LineBreakMode = LineBreakMode.WordWrap };
        button.SetBinding(Microsoft.Maui.Controls.Button.CommandProperty, commandPath);
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.CornerRadiusProperty, PasskeyResourceKeys.CornerRadius);
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.FontFamilyProperty, PasskeyResourceKeys.FontFamily);
        SemanticProperties.SetDescription(button, text);
        return button;
    }

    private static void Themed(Label label, string colorKey)
    {
        label.SetDynamicResource(Label.TextColorProperty, colorKey);
        label.SetDynamicResource(Label.FontFamilyProperty, PasskeyResourceKeys.FontFamily);
    }
}
