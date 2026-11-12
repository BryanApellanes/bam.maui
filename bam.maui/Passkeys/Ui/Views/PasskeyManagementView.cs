namespace Bam.Maui.Passkeys;

/// <summary>
/// The passkey management screen as a reusable view: heading, the list of passkeys (name, when it was added, when it
/// was last used, the device when the backend reports one, and a tag on the one this session used), each with Rename
/// and Remove; the empty state; adding another passkey; the rename panel; the removal confirmation; Refresh and Sign
/// out.  Host it in any page, or use <see cref="PasskeyManagementPage"/>.  Call
/// <see cref="PasskeyManagementViewModel.LoadAsync"/> when it appears.
/// </summary>
public sealed class PasskeyManagementView : PasskeyScreenLayout
{
    /// <summary>Creates the view.</summary>
    /// <param name="viewModel">The management view model; it becomes the binding context.</param>
    /// <param name="theme">The app's passkey theme.</param>
    public PasskeyManagementView(PasskeyManagementViewModel viewModel, PasskeyTheme theme)
        : base(theme, (viewModel ?? throw new ArgumentNullException(nameof(viewModel))).Branding, Build(viewModel))
    {
        ViewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>The view model.</summary>
    public PasskeyManagementViewModel ViewModel { get; }

    private static View Build(PasskeyManagementViewModel viewModel)
    {
        PasskeyText text = viewModel.Text;
        VerticalStackLayout screen = new VerticalStackLayout { Spacing = 16 };
        screen.Children.Add(PasskeyViewParts.Heading(nameof(PasskeyManagementViewModel.Title)));
        screen.Children.Add(PasskeyViewParts.Body(nameof(PasskeyManagementViewModel.Intro)));

        Label note = PasskeyViewParts.Body(nameof(PasskeyFlowViewModel.Note), PasskeyResourceKeys.Text);
        PasskeyViewParts.ShowWhen(note, nameof(PasskeyFlowViewModel.HasNote));
        screen.Children.Add(note);

        VerticalStackLayout list = new VerticalStackLayout { Spacing = 12 };
        BindableLayout.SetItemsSource(list, viewModel.Passkeys);
        BindableLayout.SetItemTemplate(list, new DataTemplate(Row));
        PasskeyViewParts.ShowWhen(list, nameof(PasskeyManagementViewModel.ShowsList));
        screen.Children.Add(list);

        VerticalStackLayout empty = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                PasskeyViewParts.Fixed(text.NoPasskeysTitle, 20, PasskeyResourceKeys.Text),
                PasskeyViewParts.Fixed(text.NoPasskeysMessage)
            }
        };
        PasskeyViewParts.ShowWhen(empty, nameof(PasskeyManagementViewModel.ShowsEmptyState));
        screen.Children.Add(empty);

        VerticalStackLayout add = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                PasskeyViewParts.Field(text.PasskeyNameLabel, nameof(PasskeyManagementViewModel.NewPasskeyName), text.PasskeyNamePlaceholder, Keyboard.Text, true),
                PasskeyViewParts.Primary(text.AddPasskey, nameof(PasskeyManagementViewModel.AddCommand))
            }
        };
        PasskeyViewParts.ShowWhen(add, nameof(PasskeyManagementViewModel.ShowsAdd));
        screen.Children.Add(add);

        VerticalStackLayout editor = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                PasskeyViewParts.Field(text.PasskeyNameLabel, nameof(PasskeyManagementViewModel.EditName), text.PasskeyNamePlaceholder, Keyboard.Text, true),
                PasskeyViewParts.Primary(text.Save, nameof(PasskeyManagementViewModel.SaveNameCommand)),
                PasskeyViewParts.Link(text.Cancel, nameof(PasskeyManagementViewModel.CancelEditCommand))
            }
        };
        PasskeyViewParts.ShowWhen(editor, nameof(PasskeyManagementViewModel.ShowsEditor));
        screen.Children.Add(editor);

        Label warning = PasskeyViewParts.Body(nameof(PasskeyManagementViewModel.RemovalWarning), PasskeyResourceKeys.Text);
        PasskeyViewParts.ShowWhen(warning, nameof(PasskeyManagementViewModel.HasRemovalWarning));
        Label removeTitle = PasskeyViewParts.Fixed(text.RemoveTitle, 20, PasskeyResourceKeys.Text);
        SemanticProperties.SetHeadingLevel(removeTitle, SemanticHeadingLevel.Level2);
        VerticalStackLayout removal = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                removeTitle,
                PasskeyViewParts.Fixed(text.RemoveMessage),
                warning,
                PasskeyViewParts.Primary(text.Remove, nameof(PasskeyManagementViewModel.ConfirmRemoveCommand)),
                PasskeyViewParts.Link(text.Keep, nameof(PasskeyManagementViewModel.KeepCommand))
            }
        };
        PasskeyViewParts.ShowWhen(removal, nameof(PasskeyManagementViewModel.ShowsRemoval));
        screen.Children.Add(removal);

        screen.Children.Add(PasskeyViewParts.Link(text.Refresh, nameof(PasskeyManagementViewModel.RefreshCommand)));
        screen.Children.Add(PasskeyViewParts.Secondary(text.SignOut, nameof(PasskeyManagementViewModel.SignOutCommand)));
        PasskeyViewParts.ShowWhen(screen, nameof(PasskeyFlowViewModel.IsReady));

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children = { screen, new PasskeyStatusView(text, viewModel.Configuration.Experience.AlternativeSignInLabel) }
        };

        View Row()
        {
            Label title = PasskeyViewParts.Body(nameof(PasskeyItem.Title), PasskeyResourceKeys.Text);
            title.FontAttributes = FontAttributes.Bold;
            Label current = PasskeyViewParts.Body(nameof(PasskeyItem.CurrentText), PasskeyResourceKeys.Primary);
            current.SetBinding(VisualElement.IsVisibleProperty, nameof(PasskeyItem.IsCurrent));
            Label added = PasskeyViewParts.Body(nameof(PasskeyItem.AddedText));
            PasskeyViewParts.ShowWhen(added, nameof(PasskeyItem.HasAddedText));
            Label used = PasskeyViewParts.Body(nameof(PasskeyItem.LastUsedText));
            PasskeyViewParts.ShowWhen(used, nameof(PasskeyItem.HasLastUsedText));
            Label device = PasskeyViewParts.Body(nameof(PasskeyItem.DeviceText));
            PasskeyViewParts.ShowWhen(device, nameof(PasskeyItem.HasDeviceText));
            HorizontalStackLayout actions = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    PasskeyViewParts.Link(text.Rename, nameof(PasskeyItem.RenameCommand)),
                    PasskeyViewParts.Link(text.Remove, nameof(PasskeyItem.RemoveCommand))
                }
            };
            return new VerticalStackLayout
            {
                Spacing = 4,
                Padding = new Thickness(0, 8),
                Children = { title, current, added, used, device, actions }
            };
        }
    }
}
