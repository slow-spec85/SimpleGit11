using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public sealed partial class SettingsViewModel : AppNotificationViewModelBase
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IGitService _gitService;
    private readonly ISettingsService _settingsService;
    private bool _isInitializing = true;
    public SettingsViewModel(
        MainWindowViewModel mainWindowViewModel,
        IThemeService themeService,
        ILocalizationService localizationService,
        ISettingsService settingsService,
        IGitService gitService,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _mainWindowViewModel = mainWindowViewModel;
        _themeService = themeService;
        _localizationService = localizationService;
        _settingsService = settingsService;
        _gitService = gitService;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        ThemeOptions =
        [
            new(AppThemeMode.System, _localizationService.GetString("ThemeNameSystem")),
            new(AppThemeMode.Light, _localizationService.GetString("ThemeNameLight")),
            new(AppThemeMode.Dark, _localizationService.GetString("ThemeNameDark"))
        ];
        SelectedTheme = ThemeOptions.First(option => option.Value == _themeService.CurrentTheme);
        LanguageOptions =
        [
            new(AppLanguage.System, _localizationService.GetString("LanguageSystem")),
            new(AppLanguage.English, _localizationService.GetString("LanguageEnglish")),
            new(AppLanguage.Russian, _localizationService.GetString("LanguageRussian"))
        ];
        SelectedLanguage = LanguageOptions.First(option => option.Value == _localizationService.CurrentLanguage);
        EditorFontFamilyOptions =
        [
            "Consolas",
            "Cascadia Mono",
            "Courier New"
        ];
        SelectedEditorFontFamily = EditorFontFamilyOptions.Contains(
            _settingsService.Current.EditorFontFamily,
            StringComparer.Ordinal)
                ? _settingsService.Current.EditorFontFamily
                : AppSettings.DefaultEditorFontFamily;
        EditorFontSize = _settingsService.Current.EditorFontSize;
        EditorLineSpacing = _settingsService.Current.EditorLineSpacing;
        RepositoryUserName = "";
        GlobalRepositoryUserName = "";
        RepositoryEmail = "";
        GlobalRepositoryEmail = "";
        InitialBranchName = "";
        GlobalPushDefaultRemote = "";
        RepositoryPushDefaultRemote = "";
        CredentialHelperStatus = "";
        RepositorySettingsStatus = "";
        _isInitializing = false;

    }

    public ObservableCollection<DisplayOption<AppThemeMode>> ThemeOptions { get; }
    public ObservableCollection<DisplayOption<AppLanguage>> LanguageOptions { get; }
    public ObservableCollection<string> EditorFontFamilyOptions { get; }

    [ObservableProperty]
    public partial DisplayOption<AppThemeMode> SelectedTheme { get; set; }

    [ObservableProperty]
    public partial DisplayOption<AppLanguage> SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial string SelectedEditorFontFamily { get; set; }

    [ObservableProperty]
    public partial double EditorFontSize { get; set; }

    [ObservableProperty]
    public partial double EditorLineSpacing { get; set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartRequired { get; private set; }

    [ObservableProperty]
    public partial string RepositoryUserName { get; set; }

    [ObservableProperty]
    public partial string GlobalRepositoryUserName { get; set; }

    [ObservableProperty]
    public partial string RepositoryEmail { get; set; }

    [ObservableProperty]
    public partial string GlobalRepositoryEmail { get; set; }

    [ObservableProperty]
    public partial string InitialBranchName { get; set; }

    [ObservableProperty]
    public partial string GlobalPushDefaultRemote { get; set; }

    [ObservableProperty]
    public partial string RepositoryPushDefaultRemote { get; set; }

    [ObservableProperty]
    public partial bool UseCredentialHelperManager { get; set; }

    [ObservableProperty]
    public partial string CredentialHelperStatus { get; private set; }

    [ObservableProperty]
    public partial string RepositorySettingsStatus { get; private set; }

    partial void OnSelectedThemeChanged(DisplayOption<AppThemeMode> value)
    {
        if (!_isInitializing)
        {
            _themeService.SetTheme(value.Value);
        }
    }

    partial void OnSelectedLanguageChanged(DisplayOption<AppLanguage> value)
    {
        if (!_isInitializing)
        {
            _localizationService.SetLanguage(value.Value);
            IsLanguageRestartRequired = true;
        }
    }

    partial void OnSelectedEditorFontFamilyChanged(string value)
    {
        SaveEditorAppearance(value, EditorFontSize);
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        SaveEditorAppearance(SelectedEditorFontFamily, value);
    }

    partial void OnEditorLineSpacingChanged(double value)
    {
        if (!_isInitializing && double.IsFinite(value))
        {
            _settingsService.SetEditorLineSpacing((int)Math.Round(value));
        }
    }

    private void SaveEditorAppearance(string fontFamily, double fontSize)
    {
        if (!_isInitializing)
        {
            _settingsService.SetEditorFont(fontFamily, (int)Math.Round(fontSize));
        }
    }

    partial void OnUseCredentialHelperManagerChanged(bool value)
    {
        UpdateCredentialHelperStatus();
    }

    private async Task ReadGitConfig()
    {
        ClearNotification();

        try
        {
            GlobalRepositoryUserName = await _gitService.Configuration.GetUserNameAsync(ConfigScope.Global, null) ?? "";
            GlobalRepositoryEmail = await _gitService.Configuration.GetUserEmailAsync(ConfigScope.Global, null) ?? "";
            InitialBranchName = await _gitService.Configuration.GetInitialBranchNameAsync(ConfigScope.Global, null) ?? "";
            GlobalPushDefaultRemote = await _gitService.Configuration.GetPushDefaultRemoteAsync(
                ConfigScope.Global,
                null) ?? "";
            UseCredentialHelperManager = await _gitService.Configuration.IsGlobalCredentialHelperManagerConfiguredAsync();

            RepositoryInfo? currentRepository = _mainWindowViewModel.CurrentRepository;
            if (currentRepository is not null)
            {
                RepositoryUserName = await _gitService.Configuration.GetUserNameAsync(ConfigScope.Local, currentRepository) ?? "";
                RepositoryEmail = await _gitService.Configuration.GetUserEmailAsync(ConfigScope.Local, currentRepository) ?? "";
                RepositoryPushDefaultRemote = await _gitService.Configuration.GetPushDefaultRemoteAsync(
                    ConfigScope.Local,
                    currentRepository) ?? "";
            }
            else
            {
                RepositoryUserName = "";
                RepositoryEmail = "";
                RepositoryPushDefaultRemote = "";
            }
        }
        catch (Exception exception)
        {
            ShowGitConfigError(exception, "GitConfigReadFailed");
        }
    }

    public Task RefreshSettingsAsync()
    {
        return ReadGitConfig();
    }

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnSaveRepositorySettingsAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(SaveRepositorySettingsCoreAsync);
    }

    private async Task SaveRepositorySettingsCoreAsync()
    {
        ClearNotification();
        RepositorySettingsStatus = "";

        try
        {
            await SaveGlobalGitSettingsAsync();
            await SaveCurrentRepositoryGitSettingsAsync();
            await _mainWindowViewModel.RefreshCurrentUserAsync();
            await SaveCredentialHelperSettingsAsync();

            RepositorySettingsStatus = _localizationService.GetString("RepositorySettingsSaved");
        }
        catch (Exception exception)
        {
            ShowGitConfigError(exception, "GitConfigSaveFailed");
        }
    }

    private void ShowGitConfigError(Exception exception, string fallbackResourceKey)
    {
        string message = exception switch
        {
            FileNotFoundException => _localizationService.GetString("GitExecutableNotFound"),
            DirectoryNotFoundException => _localizationService.GetString("RepositoryFolderNotFound"),
            _ => _localizationService.GetString(fallbackResourceKey)
        };
        string? details = exception is GitCommandException ? exception.Message : null;

        ShowNotification(AppNotificationSeverity.Error, message, details);
    }

    private async Task SaveCredentialHelperSettingsAsync()
    {
        if (UseCredentialHelperManager)
        {
            await _gitService.Configuration.SetGlobalCredentialHelperManagerAsync();
            return;
        }

        await _gitService.Configuration.UnsetGlobalCredentialHelperAsync();
    }

    private void UpdateCredentialHelperStatus()
    {
        CredentialHelperStatus = UseCredentialHelperManager
            ? _localizationService.GetString("CredentialHelperStatusEnabled")
            : _localizationService.GetString("CredentialHelperStatusDisabled");
    }

    private async Task SaveGlobalGitSettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(InitialBranchName))
        {
            await _gitService.Configuration.UnsetInitialBranchNameAsync(ConfigScope.Global, null);
        }
        else
        {
            await _gitService.Configuration.SetInitialBranchNameAsync(ConfigScope.Global, null, InitialBranchName.Trim());
        }

        InitialBranchName = await _gitService.Configuration.GetInitialBranchNameAsync(ConfigScope.Global, null) ?? "";

        if (string.IsNullOrWhiteSpace(GlobalPushDefaultRemote))
        {
            await _gitService.Configuration.UnsetPushDefaultRemoteAsync(ConfigScope.Global, null);
        }
        else
        {
            await _gitService.Configuration.SetPushDefaultRemoteAsync(
                ConfigScope.Global,
                null,
                GlobalPushDefaultRemote.Trim());
        }

        GlobalPushDefaultRemote = await _gitService.Configuration.GetPushDefaultRemoteAsync(
            ConfigScope.Global,
            null) ?? "";

        if (string.IsNullOrWhiteSpace(GlobalRepositoryUserName))
        {
            await _gitService.Configuration.UnsetUserNameAsync(ConfigScope.Global, null);
        }
        else
        {
            await _gitService.Configuration.SetUserNameAsync(ConfigScope.Global, null, GlobalRepositoryUserName);
        }

        if (string.IsNullOrWhiteSpace(GlobalRepositoryEmail))
        {
            await _gitService.Configuration.UnsetUserEmailAsync(ConfigScope.Global, null);
        }
        else
        {
            await _gitService.Configuration.SetUserEmailAsync(ConfigScope.Global, null, GlobalRepositoryEmail);
        }
    }

    private async Task SaveCurrentRepositoryGitSettingsAsync()
    {
        RepositoryInfo? currentRepository = _mainWindowViewModel.CurrentRepository;
        if (currentRepository is not null)
        {
            if (string.IsNullOrWhiteSpace(RepositoryPushDefaultRemote))
            {
                await _gitService.Configuration.UnsetPushDefaultRemoteAsync(
                    ConfigScope.Local,
                    currentRepository);
            }
            else
            {
                await _gitService.Configuration.SetPushDefaultRemoteAsync(
                    ConfigScope.Local,
                    currentRepository,
                    RepositoryPushDefaultRemote.Trim());
            }

            RepositoryPushDefaultRemote = await _gitService.Configuration.GetPushDefaultRemoteAsync(
                ConfigScope.Local,
                currentRepository) ?? "";

            if (string.IsNullOrWhiteSpace(RepositoryUserName))
            {
                await _gitService.Configuration.UnsetUserNameAsync(ConfigScope.Local, currentRepository);
            }
            else
            {
                await _gitService.Configuration.SetUserNameAsync(ConfigScope.Local, currentRepository, RepositoryUserName);
            }

            if (string.IsNullOrWhiteSpace(RepositoryEmail))
            {
                await _gitService.Configuration.UnsetUserEmailAsync(ConfigScope.Local, currentRepository);
            }
            else
            {
                await _gitService.Configuration.SetUserEmailAsync(ConfigScope.Local, currentRepository, RepositoryEmail);
            }
        }
    }
}
