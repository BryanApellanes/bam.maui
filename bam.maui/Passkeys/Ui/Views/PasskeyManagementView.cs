namespace Bam.Maui.Passkeys;

/// <summary>
/// The passkey management screen as a reusable view: heading, the list of passkeys (name, when it was added, when it
/// was last used, the device when the backend reports one, and a tag on the one this session used), each with Rename
/// and Remove; the empty state; adding another passkey; the rename panel; the removal confirmation; Refresh and Sign
/// out; the account-key move panel, the lost-device recovery panel, and the notice about a waiting recovery.  Host it in any page, or use <see cref="PasskeyManagementPage"/>.  Call
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

        // A waiting recovery comes first: if the person didn't start it, cancelling it is the most important thing here.
        Label pendingTitle = PasskeyViewParts.Fixed(text.PendingRecoveryTitle, 20, PasskeyResourceKeys.Text);
        SemanticProperties.SetHeadingLevel(pendingTitle, SemanticHeadingLevel.Level2);
        VerticalStackLayout pending = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                pendingTitle,
                PasskeyViewParts.Body(nameof(PasskeyManagementViewModel.PendingRecoveryText), PasskeyResourceKeys.Text),
                PasskeyViewParts.Primary(text.CancelRecovery, nameof(PasskeyManagementViewModel.CancelRecoveryCommand))
            }
        };
        PasskeyViewParts.ShowWhen(pending, nameof(PasskeyManagementViewModel.ShowsPendingRecovery));
        screen.Children.Add(pending);

        Label recoverableTitle = PasskeyViewParts.Fixed(text.KeySetRecoverableTitle, 20, PasskeyResourceKeys.Text);
        SemanticProperties.SetHeadingLevel(recoverableTitle, SemanticHeadingLevel.Level2);
        VerticalStackLayout recoverable = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                recoverableTitle,
                PasskeyViewParts.Fixed(text.KeySetRecoverableMessage),
                PasskeyViewParts.Primary(text.RecoverAccountKey, nameof(PasskeyManagementViewModel.OpenRecoveryCommand))
            }
        };
        PasskeyViewParts.ShowWhen(recoverable, nameof(PasskeyManagementViewModel.ShowsRecoverable));
        screen.Children.Add(recoverable);

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
        Label accountKeyNote = PasskeyViewParts.Body(nameof(PasskeyManagementViewModel.RemovalAccountKeyNote), PasskeyResourceKeys.Text);
        PasskeyViewParts.ShowWhen(accountKeyNote, nameof(PasskeyManagementViewModel.HasRemovalAccountKeyNote));
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
                accountKeyNote,
                PasskeyViewParts.Primary(text.Remove, nameof(PasskeyManagementViewModel.ConfirmRemoveCommand)),
                PasskeyViewParts.Link(text.Keep, nameof(PasskeyManagementViewModel.KeepCommand))
            }
        };
        PasskeyViewParts.ShowWhen(removal, nameof(PasskeyManagementViewModel.ShowsRemoval));
        screen.Children.Add(removal);

        Label moveTitle = PasskeyViewParts.Fixed(text.MoveAccountKeyTitle, 20, PasskeyResourceKeys.Text);
        SemanticProperties.SetHeadingLevel(moveTitle, SemanticHeadingLevel.Level2);
        Picker heir = HeirPicker(text);
        VerticalStackLayout handover = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                moveTitle,
                PasskeyViewParts.Fixed(text.MoveAccountKeyMessage),
                PasskeyViewParts.Fixed(text.MoveAccountKeyTo, 14, PasskeyResourceKeys.Text),
                heir,
                PasskeyViewParts.Primary(text.MoveAccountKey, nameof(PasskeyManagementViewModel.MoveAccountKeyCommand)),
                PasskeyViewParts.Secondary(text.LostDevice, nameof(PasskeyManagementViewModel.LostDeviceCommand)),
                PasskeyViewParts.Fixed(text.RemoveWithoutMovingMessage),
                PasskeyViewParts.Secondary(text.RemoveWithoutMoving, nameof(PasskeyManagementViewModel.RemoveWithoutMovingCommand)),
                PasskeyViewParts.Link(text.Keep, nameof(PasskeyManagementViewModel.CancelHandoverCommand))
            }
        };
        PasskeyViewParts.ShowWhen(handover, nameof(PasskeyManagementViewModel.ShowsHandover));
        screen.Children.Add(handover);

        Label recoveryTitle = PasskeyViewParts.Fixed(text.RecoverAccountKey, 20, PasskeyResourceKeys.Text);
        SemanticProperties.SetHeadingLevel(recoveryTitle, SemanticHeadingLevel.Level2);
        Label lostNote = PasskeyViewParts.Fixed(text.RecoveryLostMessage, 16, PasskeyResourceKeys.Text);
        PasskeyViewParts.ShowWhen(lostNote, nameof(PasskeyManagementViewModel.HasRecoveryRemoves));
        Picker recoveryHeir = HeirPicker(text);
        VerticalStackLayout keepLost = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                PasskeyViewParts.Fixed(text.RemoveWithoutMovingMessage),
                PasskeyViewParts.Secondary(text.RemoveWithoutMoving, nameof(PasskeyManagementViewModel.RemoveLostWithoutMovingCommand))
            }
        };
        PasskeyViewParts.ShowWhen(keepLost, nameof(PasskeyManagementViewModel.HasRecoveryRemoves));
        VerticalStackLayout recovery = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                recoveryTitle,
                PasskeyViewParts.Fixed(text.RecoveryMessage),
                lostNote,
                PasskeyViewParts.Fixed(text.MoveAccountKeyTo, 14, PasskeyResourceKeys.Text),
                recoveryHeir,
                PasskeyViewParts.Primary(text.StartRecovery, nameof(PasskeyManagementViewModel.StartRecoveryCommand)),
                keepLost,
                PasskeyViewParts.Link(text.Cancel, nameof(PasskeyManagementViewModel.CloseRecoveryCommand))
            }
        };
        PasskeyViewParts.ShowWhen(recovery, nameof(PasskeyManagementViewModel.ShowsRecovery));
        screen.Children.Add(recovery);

        screen.Children.Add(PasskeyViewParts.Link(text.Refresh, nameof(PasskeyManagementViewModel.RefreshCommand)));
        screen.Children.Add(PasskeyViewParts.Secondary(text.SignOut, nameof(PasskeyManagementViewModel.SignOutCommand)));
        PasskeyViewParts.ShowWhen(screen, nameof(PasskeyFlowViewModel.IsReady));

        // While the app's SignedOutAsync runs the screen says what is happening, not a blank page.
        ActivityIndicator spinner = new ActivityIndicator { IsRunning = true, HeightRequest = 32 };
        spinner.SetDynamicResource(ActivityIndicator.ColorProperty, PasskeyResourceKeys.Primary);
        VerticalStackLayout signedOut = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                PasskeyViewParts.Heading(nameof(PasskeyManagementViewModel.SignedOutTitle)),
                PasskeyViewParts.Body(nameof(PasskeyManagementViewModel.SigningOutText)),
                spinner
            }
        };
        PasskeyViewParts.ShowWhen(signedOut, nameof(PasskeyManagementViewModel.ShowsSigningOut));

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children = { screen, signedOut, new PasskeyStatusView(text, viewModel.Configuration.Experience.AlternativeSignInLabel) }
        };

        View Row()
        {
            Label title = PasskeyViewParts.Body(nameof(PasskeyItem.Title), PasskeyResourceKeys.Text);
            title.FontAttributes = FontAttributes.Bold;
            Label current = PasskeyViewParts.Body(nameof(PasskeyItem.CurrentText), PasskeyResourceKeys.Primary);
            current.SetBinding(VisualElement.IsVisibleProperty, nameof(PasskeyItem.IsCurrent));
            Label accountKey = PasskeyViewParts.Body(nameof(PasskeyItem.AccountKeyText), PasskeyResourceKeys.Primary);
            accountKey.SetBinding(VisualElement.IsVisibleProperty, nameof(PasskeyItem.IsAccountKey));
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
            Button lost = PasskeyViewParts.Link(text.LostDevice, nameof(PasskeyItem.LostCommand));
            lost.SetBinding(VisualElement.IsVisibleProperty, nameof(PasskeyItem.OffersLost));
            actions.Children.Add(lost);
            return new VerticalStackLayout
            {
                Spacing = 4,
                Padding = new Thickness(0, 8),
                Children = { title, current, accountKey, added, used, device, actions }
            };
        }
    }

    // The choice of passkey to take the account key: the move panel and the recovery panel share the view model's list.
    private static Picker HeirPicker(PasskeyText text)
    {
        Picker heir = new Picker { Title = text.MoveAccountKeyTo, MinimumHeightRequest = PasskeyViewParts.MinimumTouchTarget, ItemDisplayBinding = new Binding(nameof(PasskeyItem.Title)) };
        heir.SetBinding(Picker.ItemsSourceProperty, nameof(PasskeyManagementViewModel.HeirChoices));
        heir.SetBinding(Picker.SelectedItemProperty, nameof(PasskeyManagementViewModel.SelectedHeir), BindingMode.TwoWay);
        heir.SetDynamicResource(Picker.TextColorProperty, PasskeyResourceKeys.Text);
        heir.SetDynamicResource(Picker.FontFamilyProperty, PasskeyResourceKeys.FontFamily);
        return heir;
    }
}
