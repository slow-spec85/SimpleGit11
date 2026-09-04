using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Plugin.Ssh.Models;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Presentation;

internal sealed class SshConnectionDialogService(
    IPluginDialogHost host, ISshLocalizationService localizationService) : ISshConnectionDialogService
{
    private readonly IPluginDialogHost _host = host;
    private readonly ISshLocalizationService _localizationService = localizationService;

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
        TextBox privateKeyPath = new()
        {
            Header = _localizationService.GetString("SshPrivateKeyPathHeader"),
            PlaceholderText = _localizationService.GetString("SshPrivateKeyPathPlaceholder"),
            Text = ""
        };
        AutomationProperties.SetAutomationId(privateKeyPath, "SshPrivateKeyPath");
        PasswordBox privateKeyPassphrase = new()
        {
            Header = _localizationService.GetString("SshPrivateKeyPassphraseHeader")
        };
        AutomationProperties.SetAutomationId(privateKeyPassphrase, "SshPrivateKeyPassphrase");
        CheckBox rememberProfile = new()
        {
            Content = _localizationService.GetString("SshRememberProfileCheckBox"),
            IsChecked = true
        };
        AutomationProperties.SetAutomationId(rememberProfile, "SshRememberProfile");
        StackPanel content = new() { MinWidth = 420, Spacing = 12 };
        content.Children.Add(profileSelector);
        content.Children.Add(host);
        content.Children.Add(port);
        content.Children.Add(username);
        content.Children.Add(password);
        content.Children.Add(privateKeyPath);
        content.Children.Add(privateKeyPassphrase);
        content.Children.Add(rememberProfile);

        ContentDialog dialog = new()
        {
            Title = _localizationService.GetString("SshConnectionDialogTitle"),
            Content = content,
            PrimaryButtonText = _localizationService.GetString("SshConnectButton"),
            SecondaryButtonText = _localizationService.GetString("SshDeleteProfileButton"),
            CloseButtonText = _localizationService.GetString("SshCancelButton"),
            DefaultButton = ContentDialogButton.Primary
        };
        void ApplySelectedProfile()
        {
            SshConnectionProfile? selectedProfile =
                (profileSelector.SelectedItem as SshProfileOption)?.Profile;
            host.Text = selectedProfile?.Host ?? "";
            port.Value = selectedProfile?.Port ?? 22;
            username.Text = selectedProfile?.Username ?? "";
            privateKeyPath.Text = selectedProfile?.PrivateKeyPath ?? "";
            password.Password = "";
            privateKeyPassphrase.Password = "";
            rememberProfile.IsChecked = true;
            dialog.IsSecondaryButtonEnabled = selectedProfile is not null;
        }
        profileSelector.SelectionChanged += (_, _) => ApplySelectedProfile();
        ApplySelectedProfile();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(host.Text)
                || string.IsNullOrWhiteSpace(username.Text)
                || double.IsNaN(port.Value)
                || port.Value is < 1 or > 65535)
            {
                args.Cancel = true;
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
            string.IsNullOrWhiteSpace(privateKeyPath.Text) ? null : privateKeyPath.Text.Trim(),
            string.IsNullOrEmpty(privateKeyPassphrase.Password) ? null : privateKeyPassphrase.Password,
            endpointUnchanged ? profile!.ExpectedHostKey : null,
            rememberProfile.IsChecked == true);
    }

    private sealed record SshProfileOption(
        string DisplayName,
        SshConnectionProfile? Profile);
}
