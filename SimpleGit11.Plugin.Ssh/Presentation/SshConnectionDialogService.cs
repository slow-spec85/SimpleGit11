using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Plugin.Ssh.Models;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Presentation;

internal sealed class SshConnectionDialogService(
    IPluginDialogHost host,
    IPluginStoragePicker storagePicker,
    ISshLocalizationService localizationService,
    ISshPrivateKeyService privateKeyService) : ISshConnectionDialogService
{
    private readonly IPluginDialogHost _host = host;
    private readonly IPluginStoragePicker _storagePicker = storagePicker;
    private readonly ISshLocalizationService _localizationService = localizationService;
    private readonly ISshPrivateKeyService _privateKeyService = privateKeyService;

    public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText) =>
        _host.ConfirmAsync(title, message, primaryButtonText);

    public async Task<SshConnectionDialogResult?> ShowSshConnectionDialogAsync(
        IReadOnlyList<SshConnectionProfile> profiles,
        string? selectedProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        SshProfileOption[] profileOptions =
        [
            new(_localizationService.GetString("SshNewConnectionOption"), null),
            .. profiles.Select(profile => new SshProfileOption(
                $"{profile.Username}@{profile.Host}:{profile.Port}",
                profile))
        ];
        ComboBox profileSelector = new()
        {
            Header = _localizationService.GetString("SshSavedProfileHeader"),
            DisplayMemberPath = nameof(SshProfileOption.DisplayName),
            ItemsSource = profileOptions,
            SelectedItem = profileOptions.FirstOrDefault(option => string.Equals(
                option.Profile?.Id,
                selectedProfileId,
                StringComparison.Ordinal))
                ?? profileOptions.FirstOrDefault(option => option.Profile is not null)
                ?? profileOptions[0]
        };
        AutomationProperties.SetAutomationId(profileSelector, "SshSavedProfile");
        TextBox host = new()
        {
            Header = _localizationService.GetString("SshHostHeader"),
            PlaceholderText = _localizationService.GetString("SshHostPlaceholder"),
            Text = ""
        };
        AutomationProperties.SetAutomationId(host, "SshHost");
        NumberBox port = new()
        {
            Header = _localizationService.GetString("SshPortHeader"),
            Minimum = 1,
            Maximum = 65535,
            Value = 22,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        AutomationProperties.SetAutomationId(port, "SshPort");
        Grid hostAndPort = new() { ColumnSpacing = 12 };
        hostAndPort.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        hostAndPort.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(120)
        });
        Grid.SetColumn(port, 1);
        hostAndPort.Children.Add(host);
        hostAndPort.Children.Add(port);
        TextBox username = new()
        {
            Header = _localizationService.GetString("SshUsernameHeader"),
            Text = ""
        };
        AutomationProperties.SetAutomationId(username, "SshUsername");
        PasswordBox password = new()
        {
            Header = _localizationService.GetString("SshPasswordHeader")
        };
        AutomationProperties.SetAutomationId(password, "SshPassword");
        Grid credentials = new() { ColumnSpacing = 12 };
        credentials.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        credentials.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        Grid.SetColumn(password, 1);
        credentials.Children.Add(username);
        credentials.Children.Add(password);
        string privateKeyModeLabel = _localizationService.GetString("SshPrivateKeyModeHeader");
        ToggleSwitch privateKeyMode = new()
        {
            OnContent = privateKeyModeLabel,
            OffContent = privateKeyModeLabel,
            Height = 32,
            MinHeight = 32,
            IsOn = false
        };
        AutomationProperties.SetAutomationId(privateKeyMode, "SshPrivateKeyMode");
        AutomationProperties.SetName(privateKeyMode, privateKeyModeLabel);
        TextBox privateKeyPath = new()
        {
            Header = _localizationService.GetString("SshPrivateKeyPathHeader"),
            PlaceholderText = _localizationService.GetString("SshPrivateKeyPathPlaceholder"),
            Text = ""
        };
        AutomationProperties.SetAutomationId(privateKeyPath, "SshPrivateKeyPath");
        string generatePrivateKeyLabel = _localizationService.GetString("SshGeneratePrivateKeyButton");
        Button generatePrivateKey = new()
        {
            Content = new FontIcon { Glyph = "\uE8A5", FontSize = 16 },
            Width = 40,
            Height = 32,
            MinWidth = 40,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        AutomationProperties.SetAutomationId(generatePrivateKey, "SshGeneratePrivateKey");
        AutomationProperties.SetName(generatePrivateKey, generatePrivateKeyLabel);
        ToolTipService.SetToolTip(generatePrivateKey, generatePrivateKeyLabel);
        PasswordBox generatedKeyPassphrase = new()
        {
            Header = _localizationService.GetString("SshGeneratedKeyPassphraseHeader")
        };
        AutomationProperties.SetAutomationId(
            generatedKeyPassphrase,
            "SshGeneratedKeyPassphrase");
        PasswordBox generatedKeyPassphraseConfirmation = new()
        {
            Header = _localizationService.GetString("SshGeneratedKeyPassphraseConfirmationHeader")
        };
        AutomationProperties.SetAutomationId(
            generatedKeyPassphraseConfirmation,
            "SshGeneratedKeyPassphraseConfirmation");
        InfoBar generatedKeyPassphraseValidation = new()
        {
            IsClosable = false,
            IsOpen = false,
            Severity = InfoBarSeverity.Error,
            Message = _localizationService.GetString("SshGeneratedKeyPassphraseMismatchMessage")
        };
        AutomationProperties.SetAutomationId(
            generatedKeyPassphraseValidation,
            "SshGeneratedKeyPassphraseValidation");
        Button confirmKeyGeneration = new()
        {
            Content = _localizationService.GetString("SshGeneratedKeyPassphraseContinueButton")
        };
        AutomationProperties.SetAutomationId(confirmKeyGeneration, "SshConfirmKeyGeneration");
        Button cancelKeyGeneration = new()
        {
            Content = _localizationService.GetString("SshCancelButton")
        };
        AutomationProperties.SetAutomationId(cancelKeyGeneration, "SshCancelKeyGeneration");
        StackPanel keyGenerationButtons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        keyGenerationButtons.Children.Add(confirmKeyGeneration);
        keyGenerationButtons.Children.Add(cancelKeyGeneration);
        StackPanel keyGenerationPrompt = new() { Width = 320, Spacing = 12 };
        keyGenerationPrompt.Children.Add(new TextBlock
        {
            Text = _localizationService.GetString("SshGeneratedKeyPassphrasePromptTitle"),
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style
        });
        keyGenerationPrompt.Children.Add(new TextBlock
        {
            Text = _localizationService.GetString("SshGeneratedKeyPassphrasePromptMessage"),
            TextWrapping = TextWrapping.WrapWholeWords
        });
        keyGenerationPrompt.Children.Add(generatedKeyPassphrase);
        keyGenerationPrompt.Children.Add(generatedKeyPassphraseConfirmation);
        keyGenerationPrompt.Children.Add(generatedKeyPassphraseValidation);
        keyGenerationPrompt.Children.Add(keyGenerationButtons);
        Flyout keyGenerationFlyout = new() { Content = keyGenerationPrompt };
        keyGenerationFlyout.Opened += (_, _) =>
            generatedKeyPassphrase.Focus(FocusState.Programmatic);
        keyGenerationFlyout.Closed += (_, _) =>
        {
            generatedKeyPassphrase.Password = "";
            generatedKeyPassphraseConfirmation.Password = "";
            generatedKeyPassphraseValidation.IsOpen = false;
        };
        Button browsePrivateKey = new()
        {
            Content = new FontIcon { Glyph = "\uE8B7", FontSize = 16 },
            Width = 40,
            Height = 32,
            MinWidth = 40,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        string browsePrivateKeyLabel = _localizationService.GetString("SshBrowsePrivateKeyButton");
        AutomationProperties.SetAutomationId(browsePrivateKey, "SshBrowsePrivateKey");
        AutomationProperties.SetName(browsePrivateKey, browsePrivateKeyLabel);
        ToolTipService.SetToolTip(browsePrivateKey, browsePrivateKeyLabel);
        Grid privateKeyPathRow = new() { ColumnSpacing = 8 };
        privateKeyPathRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        privateKeyPathRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        privateKeyPathRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        Grid.SetColumn(generatePrivateKey, 1);
        Grid.SetColumn(browsePrivateKey, 2);
        privateKeyPathRow.Children.Add(privateKeyPath);
        privateKeyPathRow.Children.Add(generatePrivateKey);
        privateKeyPathRow.Children.Add(browsePrivateKey);
        PasswordBox privateKeyPassphrasePrompt = new()
        {
            Header = _localizationService.GetString("SshGeneratedKeyPassphraseHeader")
        };
        AutomationProperties.SetAutomationId(
            privateKeyPassphrasePrompt,
            "SshPrivateKeyPassphrasePrompt");
        InfoBar privateKeyPassphraseValidation = new()
        {
            IsClosable = false,
            IsOpen = false,
            Severity = InfoBarSeverity.Error,
            Message = _localizationService.GetString("SshPrivateKeyPassphraseInvalidMessage")
        };
        AutomationProperties.SetAutomationId(
            privateKeyPassphraseValidation,
            "SshPrivateKeyPassphraseValidation");
        Button confirmPrivateKeyPassphrase = new()
        {
            Content = _localizationService.GetString("SshGeneratedKeyPassphraseContinueButton")
        };
        AutomationProperties.SetAutomationId(
            confirmPrivateKeyPassphrase,
            "SshConfirmPrivateKeyPassphrase");
        Button cancelPrivateKeyPassphrase = new()
        {
            Content = _localizationService.GetString("SshCancelButton")
        };
        AutomationProperties.SetAutomationId(
            cancelPrivateKeyPassphrase,
            "SshCancelPrivateKeyPassphrase");
        StackPanel privateKeyPassphraseButtons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        privateKeyPassphraseButtons.Children.Add(confirmPrivateKeyPassphrase);
        privateKeyPassphraseButtons.Children.Add(cancelPrivateKeyPassphrase);
        StackPanel privateKeyPassphraseContent = new() { Width = 320, Spacing = 12 };
        privateKeyPassphraseContent.Children.Add(new TextBlock
        {
            Text = _localizationService.GetString("SshPrivateKeyPassphrasePromptTitle"),
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style
        });
        privateKeyPassphraseContent.Children.Add(new TextBlock
        {
            Text = _localizationService.GetString("SshPrivateKeyPassphrasePromptMessage"),
            TextWrapping = TextWrapping.WrapWholeWords
        });
        privateKeyPassphraseContent.Children.Add(privateKeyPassphrasePrompt);
        privateKeyPassphraseContent.Children.Add(privateKeyPassphraseValidation);
        privateKeyPassphraseContent.Children.Add(privateKeyPassphraseButtons);
        Flyout privateKeyPassphraseFlyout = new() { Content = privateKeyPassphraseContent };
        privateKeyPassphraseFlyout.Opened += (_, _) =>
            privateKeyPassphrasePrompt.Focus(FocusState.Programmatic);
        InfoBar keyInstallationNotice = new()
        {
            IsClosable = false,
            IsOpen = true,
            Severity = InfoBarSeverity.Informational,
            Message = _localizationService.GetString("SshPrivateKeyPasswordNotice")
        };
        AutomationProperties.SetAutomationId(keyInstallationNotice, "SshPrivateKeyPasswordNotice");
        StackPanel privateKeyFields = new() { Spacing = 8, Visibility = Visibility.Collapsed };
        privateKeyFields.Children.Add(privateKeyPathRow);
        privateKeyFields.Children.Add(keyInstallationNotice);
        InfoBar validationMessage = new()
        {
            IsClosable = false,
            IsOpen = false,
            Severity = InfoBarSeverity.Error
        };
        AutomationProperties.SetAutomationId(validationMessage, "SshConnectionValidation");
        string rememberProfileLabel = _localizationService.GetString("SshRememberProfileToggleSwitch");
        ToggleSwitch rememberProfile = new()
        {
            OnContent = rememberProfileLabel,
            OffContent = rememberProfileLabel,
            Height = 32,
            MinHeight = 32,
            Margin = new Thickness(0, -4, 0, 0),
            IsOn = true
        };
        AutomationProperties.SetAutomationId(rememberProfile, "SshRememberProfile");
        AutomationProperties.SetName(rememberProfile, rememberProfileLabel);
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(profileSelector);
        content.Children.Add(hostAndPort);
        content.Children.Add(credentials);
        content.Children.Add(privateKeyMode);
        content.Children.Add(validationMessage);
        content.Children.Add(privateKeyFields);
        content.Children.Add(rememberProfile);
        ScrollViewer scrollViewer = new()
        {
            Content = content,
            Width = 468,
            MaxHeight = 440,
            Margin = new Thickness(-24, 0, -24, -8),
            Padding = new Thickness(24, 0, 24, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };

        ContentDialog dialog = new()
        {
            Title = _localizationService.GetString("SshConnectionDialogTitle"),
            Content = scrollViewer,
            PrimaryButtonText = _localizationService.GetString("SshConnectButton"),
            SecondaryButtonText = _localizationService.GetString("SshDeleteProfileButton"),
            CloseButtonText = _localizationService.GetString("SshCancelButton"),
            DefaultButton = ContentDialogButton.Primary
        };
        string? privateKeyPassphrase = null;
        TaskCompletionSource<string?>? privateKeyPassphraseCompletion = null;
        privateKeyPassphraseFlyout.Closed += (_, _) =>
        {
            privateKeyPassphraseCompletion?.TrySetResult(null);
            privateKeyPassphrasePrompt.Password = "";
            privateKeyPassphraseValidation.IsOpen = false;
        };
        confirmPrivateKeyPassphrase.Click += async (_, _) =>
        {
            string candidate = privateKeyPassphrasePrompt.Password;
            if (string.IsNullOrEmpty(candidate)
                || !await _privateKeyService.CanOpenAsync(privateKeyPath.Text.Trim(), candidate))
            {
                privateKeyPassphraseValidation.IsOpen = true;
                return;
            }

            privateKeyPassphraseCompletion?.TrySetResult(candidate);
            privateKeyPassphraseFlyout.Hide();
        };
        cancelPrivateKeyPassphrase.Click += (_, _) => privateKeyPassphraseFlyout.Hide();
        async Task<string?> RequestPrivateKeyPassphraseAsync()
        {
            privateKeyPassphrasePrompt.Password = "";
            privateKeyPassphraseValidation.IsOpen = false;
            privateKeyPassphraseCompletion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            privateKeyPassphraseFlyout.ShowAt(privateKeyPath);
            return await privateKeyPassphraseCompletion.Task;
        }
        bool keyMaterialChanged = false;
        bool IsKeyVerifiedForCurrentValues()
        {
            SshConnectionProfile? selectedProfile =
                (profileSelector.SelectedItem as SshProfileOption)?.Profile;
            return !keyMaterialChanged
                && selectedProfile?.PrivateKeyPath is not null
                && string.Equals(selectedProfile.Host, host.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                && selectedProfile.Port == (int)port.Value
                && string.Equals(selectedProfile.Username, username.Text.Trim(), StringComparison.Ordinal)
                && string.Equals(
                    selectedProfile.PrivateKeyPath,
                    privateKeyPath.Text.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
        void UpdatePrivateKeyState()
        {
            SshConnectionProfile? selectedProfile =
                (profileSelector.SelectedItem as SshProfileOption)?.Profile;
            bool isSavedProfile = selectedProfile is not null;
            bool usesPrivateKey = privateKeyMode.IsOn;
            bool isVerified = usesPrivateKey && IsKeyVerifiedForCurrentValues();
            bool showServerPassword = !usesPrivateKey || !isSavedProfile || !isVerified;
            bool canEditIdentity = !isSavedProfile;
            privateKeyFields.Visibility = usesPrivateKey
                ? Visibility.Visible
                : Visibility.Collapsed;
            keyInstallationNotice.IsOpen = usesPrivateKey && !isVerified;
            password.Visibility = showServerPassword
                ? Visibility.Visible
                : Visibility.Collapsed;
            Grid.SetColumnSpan(username, showServerPassword ? 1 : 2);
            host.IsReadOnly = !canEditIdentity;
            port.IsEnabled = canEditIdentity;
            username.IsReadOnly = !canEditIdentity;
            rememberProfile.Visibility = !isSavedProfile
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        void ApplySelectedProfile()
        {
            SshConnectionProfile? selectedProfile =
                (profileSelector.SelectedItem as SshProfileOption)?.Profile;
            host.Text = selectedProfile?.Host ?? "";
            port.Value = selectedProfile?.Port ?? 22;
            username.Text = selectedProfile?.Username ?? "";
            privateKeyPath.Text = selectedProfile?.PrivateKeyPath ?? "";
            privateKeyMode.IsOn = selectedProfile?.PrivateKeyPath is not null;
            password.Password = "";
            privateKeyPassphrase = null;
            rememberProfile.IsOn = true;
            keyMaterialChanged = false;
            dialog.IsSecondaryButtonEnabled = selectedProfile is not null;
            validationMessage.IsOpen = false;
            UpdatePrivateKeyState();
        }
        profileSelector.SelectionChanged += (_, _) => ApplySelectedProfile();
        privateKeyMode.Toggled += (_, _) =>
        {
            UpdatePrivateKeyState();
            validationMessage.IsOpen = false;
        };
        host.TextChanged += (_, _) => UpdatePrivateKeyState();
        port.ValueChanged += (_, _) => UpdatePrivateKeyState();
        username.TextChanged += (_, _) => UpdatePrivateKeyState();
        privateKeyPath.TextChanged += (_, _) =>
        {
            privateKeyPassphrase = null;
            UpdatePrivateKeyState();
        };
        generatePrivateKey.Click += (_, _) =>
        {
            generatedKeyPassphrase.Password = "";
            generatedKeyPassphraseConfirmation.Password = "";
            generatedKeyPassphraseValidation.IsOpen = false;
            keyGenerationFlyout.ShowAt(generatePrivateKey);
        };
        cancelKeyGeneration.Click += (_, _) => keyGenerationFlyout.Hide();
        confirmKeyGeneration.Click += async (_, _) =>
        {
            if (!string.Equals(
                    generatedKeyPassphrase.Password,
                    generatedKeyPassphraseConfirmation.Password,
                    StringComparison.Ordinal))
            {
                generatedKeyPassphraseValidation.IsOpen = true;
                return;
            }

            string? passphrase = string.IsNullOrEmpty(generatedKeyPassphrase.Password)
                ? null
                : generatedKeyPassphrase.Password;
            keyGenerationFlyout.Hide();
            string? selectedPath = await _storagePicker.PickSaveFileAsync("id_simplegit11");
            if (selectedPath is null)
            {
                return;
            }

            generatePrivateKey.IsEnabled = false;
            browsePrivateKey.IsEnabled = false;
            try
            {
                await _privateKeyService.GenerateAsync(selectedPath, passphrase);
                keyMaterialChanged = true;
                privateKeyPath.Text = selectedPath;
                privateKeyPassphrase = passphrase;
                validationMessage.IsOpen = false;
                UpdatePrivateKeyState();
            }
            catch (Exception exception)
            {
                validationMessage.Message = string.Format(
                    _localizationService.GetString("SshPrivateKeyGenerationFailedMessage"),
                    exception.Message);
                validationMessage.IsOpen = true;
            }
            finally
            {
                generatePrivateKey.IsEnabled = true;
                browsePrivateKey.IsEnabled = true;
            }
        };
        browsePrivateKey.Click += async (_, _) =>
        {
            string? selectedPath = await _storagePicker.PickFileAsync();
            if (selectedPath is not null)
            {
                privateKeyPassphrase = null;
                privateKeyPath.Text = selectedPath;
            }
        };
        ApplySelectedProfile();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            string? errorMessage = null;
            if (string.IsNullOrWhiteSpace(host.Text)
                || string.IsNullOrWhiteSpace(username.Text)
                || double.IsNaN(port.Value)
                || port.Value is < 1 or > 65535)
            {
                errorMessage = _localizationService.GetString("SshConnectionFieldsRequiredMessage");
            }
            else if (!privateKeyMode.IsOn && string.IsNullOrEmpty(password.Password))
            {
                errorMessage = _localizationService.GetString("SshPasswordRequiredMessage");
            }
            else if (privateKeyMode.IsOn && string.IsNullOrWhiteSpace(privateKeyPath.Text))
            {
                errorMessage = _localizationService.GetString("SshPrivateKeyRequiredMessage");
            }
            else if (privateKeyMode.IsOn
                && !IsKeyVerifiedForCurrentValues()
                && string.IsNullOrEmpty(password.Password))
            {
                errorMessage = _localizationService.GetString("SshInitialPasswordRequiredMessage");
            }

            if (errorMessage is not null)
            {
                validationMessage.Message = errorMessage;
                validationMessage.IsOpen = true;
                args.Cancel = true;
                return;
            }

            if (privateKeyMode.IsOn && privateKeyPassphrase is null)
            {
                ContentDialogButtonClickDeferral deferral = args.GetDeferral();
                try
                {
                    if (await _privateKeyService.RequiresPassphraseAsync(privateKeyPath.Text.Trim()))
                    {
                        privateKeyPassphrase = await RequestPrivateKeyPassphraseAsync();
                        args.Cancel = privateKeyPassphrase is null;
                    }
                }
                catch (Exception exception)
                {
                    validationMessage.Message = string.Format(
                        _localizationService.GetString("SshPrivateKeyReadFailedMessage"),
                        exception.Message);
                    validationMessage.IsOpen = true;
                    args.Cancel = true;
                }
                finally
                {
                    deferral.Complete();
                }
            }
        };
        ContentDialogResult dialogResult = await _host.ShowAsync(dialog);
        if (dialogResult == ContentDialogResult.None)
        {
            return null;
        }

        SshConnectionProfile? profile =
            (profileSelector.SelectedItem as SshProfileOption)?.Profile;
        if (dialogResult == ContentDialogResult.Secondary && profile is not null)
        {
            return new SshConnectionDialogResult(
                SshConnectionDialogAction.DeleteProfile,
                profile.Id,
                profile.Host,
                profile.Port,
                profile.Username,
                null,
                profile.PrivateKeyPath,
                null,
                profile.ExpectedHostKey,
                true);
        }

        bool endpointUnchanged = profile is not null
            && string.Equals(profile.Host, host.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            && profile.Port == (int)port.Value;
        return new SshConnectionDialogResult(
            SshConnectionDialogAction.Connect,
            profile?.Id ?? Guid.NewGuid().ToString("N"),
            host.Text.Trim(),
            (int)port.Value,
            username.Text.Trim(),
            string.IsNullOrEmpty(password.Password) ? null : password.Password,
            !privateKeyMode.IsOn || string.IsNullOrWhiteSpace(privateKeyPath.Text)
                ? null
                : privateKeyPath.Text.Trim(),
            !privateKeyMode.IsOn ? null : privateKeyPassphrase,
            endpointUnchanged ? profile!.ExpectedHostKey : null,
            profile is not null || rememberProfile.IsOn);
    }

    private sealed record SshProfileOption(
        string DisplayName,
        SshConnectionProfile? Profile);
}
