using System;
using System.Collections.Generic;
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
using SimpleGit11.Services.Execution;

namespace SimpleGit11.ViewModels;

public sealed partial class SettingsViewModel : AppNotificationViewModelBase
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IGitService _gitService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IExecutionContextService _executionContextService;
    private GitPullSettings? _savedGlobalPullSettings;
    private GitPullSettings? _savedRepositoryPullSettings;
    private bool _isInitializing = true;
    public SettingsViewModel(
        MainWindowViewModel mainWindowViewModel,
        IThemeService themeService,
        ILocalizationService localizationService,
        ISettingsService settingsService,
        IGitService gitService,
        IDialogService dialogService,
        IExecutionContextService executionContextService,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _mainWindowViewModel = mainWindowViewModel;
        _themeService = themeService;
        _localizationService = localizationService;
        _settingsService = settingsService;
        _gitService = gitService;
        _dialogService = dialogService;
        _executionContextService = executionContextService;
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
        SshCommand = "";
        RepositoryPushDefaultRemote = "";
        CredentialHelperStatus = "";
        RepositorySettingsStatus = "";
        GlobalUrlRewrites = [];
        GlobalPullRebaseOptions = CreatePullRebaseOptions();
        GlobalPullFastForwardOptions = CreatePullFastForwardOptions();
        RepositoryPullRebaseOptions = CreatePullRebaseOptions();
        RepositoryPullFastForwardOptions = CreatePullFastForwardOptions();
        SelectedGlobalPullRebase = GlobalPullRebaseOptions[0];
        SelectedGlobalPullFastForward = GlobalPullFastForwardOptions[0];
        SelectedRepositoryPullRebase = RepositoryPullRebaseOptions[0];
        SelectedRepositoryPullFastForward = RepositoryPullFastForwardOptions[0];
        _isInitializing = false;

    }

    public ObservableCollection<DisplayOption<AppThemeMode>> ThemeOptions { get; }
    public ObservableCollection<DisplayOption<AppLanguage>> LanguageOptions { get; }
    public ObservableCollection<string> EditorFontFamilyOptions { get; }
    public sealed record ConfigOption(string? Value, string DisplayName);

    public ObservableCollection<ConfigOption> GlobalPullRebaseOptions { get; }
    public ObservableCollection<ConfigOption> GlobalPullFastForwardOptions { get; }
    public ObservableCollection<ConfigOption> RepositoryPullRebaseOptions { get; }
    public ObservableCollection<ConfigOption> RepositoryPullFastForwardOptions { get; }

    [ObservableProperty]
    public partial ConfigOption SelectedGlobalPullRebase { get; set; }

    [ObservableProperty]
    public partial ConfigOption SelectedGlobalPullFastForward { get; set; }

    [ObservableProperty]
    public partial ConfigOption SelectedRepositoryPullRebase { get; set; }

    [ObservableProperty]
    public partial ConfigOption SelectedRepositoryPullFastForward { get; set; }

    [ObservableProperty]
    public partial bool IsGlobalPullSettingsLoaded { get; private set; }

    [ObservableProperty]
    public partial bool IsRepositoryPullSettingsLoaded { get; private set; }

    public string GlobalGitSettingsTitle => string.Format(
        _localizationService.GetString("GlobalGitSettingsTitleFormat"),
        _executionContextService.Current.DisplayMachineName);

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
    public partial string SshCommand { get; set; }

    [ObservableProperty]
    public partial bool UseSshCommandOverride { get; set; }

    [ObservableProperty]
    public partial string RepositoryPushDefaultRemote { get; set; }

    [ObservableProperty]
    public partial bool UseCredentialHelperManager { get; set; }

    [ObservableProperty]
    public partial string CredentialHelperStatus { get; private set; }

    [ObservableProperty]
    public partial string RepositorySettingsStatus { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<GitUrlRewrite> GlobalUrlRewrites { get; private set; }

    [ObservableProperty]
    public partial bool IsUrlRewriteOperationRunning { get; private set; }

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

    partial void OnIsUrlRewriteOperationRunningChanged(bool value)
    {
        AddGlobalUrlRewriteCommand.NotifyCanExecuteChanged();
        EditGlobalUrlRewriteCommand.NotifyCanExecuteChanged();
        RemoveGlobalUrlRewriteCommand.NotifyCanExecuteChanged();
    }

    private async Task ReadGitConfig()
    {
        ClearNotification();
        ResetPullSettings();

        try
        {
            GlobalRepositoryUserName = await _gitService.Configuration.GetUserNameAsync(ConfigScope.Global, null) ?? "";
            GlobalRepositoryEmail = await _gitService.Configuration.GetUserEmailAsync(ConfigScope.Global, null) ?? "";
            InitialBranchName = await _gitService.Configuration.GetInitialBranchNameAsync(ConfigScope.Global, null) ?? "";
            GlobalPushDefaultRemote = await _gitService.Configuration.GetPushDefaultRemoteAsync(
                ConfigScope.Global,
                null) ?? "";
            string configuredSshCommand = await _gitService.Configuration.GetGlobalSshCommandAsync();
            UseSshCommandOverride = !string.IsNullOrWhiteSpace(configuredSshCommand);
            SshCommand = configuredSshCommand;
            UseCredentialHelperManager = await _gitService.Configuration.IsGlobalCredentialHelperManagerConfiguredAsync();
            GlobalUrlRewrites = await _gitService.Configuration.GetGlobalUrlRewritesAsync();
            await LoadPullSettingsAsync(ConfigScope.Global, null);

            RepositoryInfo? currentRepository = _mainWindowViewModel.CurrentRepository;
            if (currentRepository is not null)
            {
                await LoadPullSettingsAsync(ConfigScope.Local, currentRepository);
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
        OnPropertyChanged(nameof(GlobalGitSettingsTitle));
        return ReadGitConfig();
    }

    private ObservableCollection<ConfigOption> CreatePullRebaseOptions() =>
    [
        CreatePullOption(null, "PullNotSetOption"),
        CreatePullOption("false", "PullMergeOption"),
        CreatePullOption("true", "PullRebaseOption"),
        CreatePullOption("merges", "PullRebaseMergesOption"),
        CreatePullOption("interactive", "PullRebaseInteractiveOption")
    ];

    private ObservableCollection<ConfigOption> CreatePullFastForwardOptions() =>
    [
        CreatePullOption(null, "PullNotSetOption"),
        CreatePullOption("true", "PullFastForwardOption"),
        CreatePullOption("false", "PullNoFastForwardOption"),
        CreatePullOption("only", "PullFastForwardOnlyOption")
    ];

    private ConfigOption CreatePullOption(string? value, string resourceKey) =>
        new(value, _localizationService.GetString(resourceKey));

    private ConfigOption FindPullOption(ObservableCollection<ConfigOption> options, string? value, int standardCount)
    {
        while (options.Count > standardCount)
        {
            options.RemoveAt(options.Count - 1);
        }

        ConfigOption? option = options.FirstOrDefault(item => item.Value == value);
        if (option is null)
        {
            string displayValue = value!.Length == 0 ? _localizationService.GetString("PullEmptyValue") : value;
            option = new ConfigOption(value, displayValue);
            options.Add(option);
        }

        return option;
    }

    private void ResetPullSettings()
    {
        IsGlobalPullSettingsLoaded = false;
        IsRepositoryPullSettingsLoaded = false;
        _savedGlobalPullSettings = null;
        _savedRepositoryPullSettings = null;
        SelectedGlobalPullRebase = GlobalPullRebaseOptions[0];
        SelectedGlobalPullFastForward = GlobalPullFastForwardOptions[0];
        SelectedRepositoryPullRebase = RepositoryPullRebaseOptions[0];
        SelectedRepositoryPullFastForward = RepositoryPullFastForwardOptions[0];
    }

    private async Task LoadPullSettingsAsync(ConfigScope scope, RepositoryInfo? repository)
    {
        GitPullSettings settings = await _gitService.Configuration.GetPullSettingsAsync(scope, repository);
        if (scope == ConfigScope.Global)
        {
            SelectedGlobalPullRebase = FindPullOption(GlobalPullRebaseOptions, settings.Rebase, 5);
            SelectedGlobalPullFastForward = FindPullOption(GlobalPullFastForwardOptions, settings.FastForward, 4);
            _savedGlobalPullSettings = settings;
            IsGlobalPullSettingsLoaded = true;
        }
        else
        {
            SelectedRepositoryPullRebase = FindPullOption(RepositoryPullRebaseOptions, settings.Rebase, 5);
            SelectedRepositoryPullFastForward = FindPullOption(RepositoryPullFastForwardOptions, settings.FastForward, 4);
            _savedRepositoryPullSettings = settings;
            IsRepositoryPullSettingsLoaded = true;
        }
    }

    private async Task SavePullSettingsAsync(ConfigScope scope, RepositoryInfo? repository)
    {
        GitPullSettings? savedSettings = scope == ConfigScope.Global
            ? _savedGlobalPullSettings
            : _savedRepositoryPullSettings;
        if (savedSettings is null)
        {
            return;
        }

        string? rebase = scope == ConfigScope.Global ? SelectedGlobalPullRebase.Value : SelectedRepositoryPullRebase.Value;
        string? fastForward = scope == ConfigScope.Global ? SelectedGlobalPullFastForward.Value : SelectedRepositoryPullFastForward.Value;
        // Preserve absent and nonstandard values until explicitly changed by the user.
        if (rebase != savedSettings.Rebase)
        {
            await _gitService.Configuration.SetPullRebaseAsync(scope, repository, rebase);
            savedSettings = savedSettings with { Rebase = rebase };
            UpdateSavedPullSettings(scope, savedSettings);
        }

        if (fastForward != savedSettings.FastForward)
        {
            await _gitService.Configuration.SetPullFastForwardAsync(scope, repository, fastForward);
            savedSettings = savedSettings with { FastForward = fastForward };
            UpdateSavedPullSettings(scope, savedSettings);
        }
    }

    private void UpdateSavedPullSettings(ConfigScope scope, GitPullSettings settings)
    {
        if (scope == ConfigScope.Global)
        {
            _savedGlobalPullSettings = settings;
        }
        else
        {
            _savedRepositoryPullSettings = settings;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageGlobalUrlRewrites), FlowExceptionsToTaskScheduler = true)]
    private Task OnAddGlobalUrlRewriteAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(AddGlobalUrlRewriteAsync);
    }

    [RelayCommand(CanExecute = nameof(CanEditGlobalUrlRewrite), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditGlobalUrlRewriteAsync(GitUrlRewrite? rewrite)
    {
        return _asyncCommandExecutor.ExecuteAsync(() => EditGlobalUrlRewriteAsync(rewrite));
    }

    private bool CanEditGlobalUrlRewrite(GitUrlRewrite? rewrite) =>
        CanManageGlobalUrlRewrites() && rewrite is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveGlobalUrlRewrite), FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveGlobalUrlRewriteAsync(GitUrlRewrite? rewrite)
    {
        return _asyncCommandExecutor.ExecuteAsync(() => RemoveGlobalUrlRewriteAsync(rewrite));
    }

    private bool CanRemoveGlobalUrlRewrite(GitUrlRewrite? rewrite) =>
        CanManageGlobalUrlRewrites() && rewrite is not null;

    private bool CanManageGlobalUrlRewrites() => !IsUrlRewriteOperationRunning;

    private async Task AddGlobalUrlRewriteAsync()
    {
        GitUrlRewrite? rewrite = await _dialogService.ShowGitUrlRewriteDialogAsync();
        if (rewrite is null || HasConflictingUrlRewrite(rewrite))
        {
            return;
        }

        await RunUrlRewriteOperationAsync(
            () => _gitService.Configuration.AddGlobalUrlRewriteAsync(rewrite),
            "GitUrlRewriteAdded");
    }

    private async Task EditGlobalUrlRewriteAsync(GitUrlRewrite? rewrite)
    {
        if (rewrite is null)
        {
            return;
        }

        GitUrlRewrite? updatedRewrite = await _dialogService.ShowGitUrlRewriteDialogAsync(rewrite);
        if (updatedRewrite is null || updatedRewrite == rewrite)
        {
            return;
        }

        if (HasConflictingUrlRewrite(updatedRewrite, rewrite))
        {
            return;
        }

        await RunUrlRewriteOperationAsync(
            () => _gitService.Configuration.UpdateGlobalUrlRewriteAsync(rewrite, updatedRewrite),
            "GitUrlRewriteUpdated");
    }

    private Task RemoveGlobalUrlRewriteAsync(GitUrlRewrite? rewrite)
    {
        return rewrite is null
            ? Task.CompletedTask
            : RunUrlRewriteOperationAsync(
                () => _gitService.Configuration.RemoveGlobalUrlRewriteAsync(rewrite),
                "GitUrlRewriteRemoved");
    }

    private bool HasConflictingUrlRewrite(
        GitUrlRewrite rewrite,
        GitUrlRewrite? ignoredRewrite = null)
    {
        bool hasConflict = GlobalUrlRewrites.Any(existing =>
            !ReferenceEquals(existing, ignoredRewrite)
            && string.Equals(
                existing.InsteadOfUrl,
                rewrite.InsteadOfUrl,
                StringComparison.Ordinal));
        if (hasConflict)
        {
            ShowNotification(
                AppNotificationSeverity.Error,
                _localizationService.GetString("GitUrlRewriteAlreadyExists"));
        }

        return hasConflict;
    }

    private async Task RunUrlRewriteOperationAsync(Func<Task> operation, string successResourceKey)
    {
        if (IsUrlRewriteOperationRunning)
        {
            return;
        }

        ClearNotification();
        IsUrlRewriteOperationRunning = true;
        try
        {
            await operation();
            GlobalUrlRewrites = await _gitService.Configuration.GetGlobalUrlRewritesAsync();
            ShowNotification(
                AppNotificationSeverity.Success,
                _localizationService.GetString(successResourceKey));
        }
        catch (Exception exception)
        {
            ShowGitConfigError(exception, "GitUrlRewriteSaveFailed");
        }
        finally
        {
            IsUrlRewriteOperationRunning = false;
        }
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
        await SavePullSettingsAsync(ConfigScope.Global, null);
        if (UseSshCommandOverride && !string.IsNullOrWhiteSpace(SshCommand))
        {
            await _gitService.Configuration.SetGlobalSshCommandAsync(SshCommand);
            SshCommand = await _gitService.Configuration.GetGlobalSshCommandAsync();
        }
        else
        {
            await _gitService.Configuration.UnsetGlobalSshCommandAsync();
            UseSshCommandOverride = false;
        }

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
            await SavePullSettingsAsync(ConfigScope.Local, currentRepository);
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
