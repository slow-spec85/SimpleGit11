using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Services;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;
using SimpleGit11.Services.Git;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Plugins;
using SimpleGit11.ViewModels;

namespace SimpleGit11;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _window;

    public App()
    {
        _services = ConfigureServices();
        ApplyStartupTheme();
        GetService<ILocalizationService>().ApplyLanguage();
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow(
                GetService<MainWindowViewModel>(),
                GetService<ILocalizationService>(),
                GetService<IAsyncCommandExecutor>(),
                GetService<IDialogService>(),
                GetService<IExecutionContextService>(),
                GetService<IGitRepositoryChangeDetector>(),
                GetService<IGitService>(),
                GetService<IMessenger>(),
                GetService<IEnumerable<IMainMenuContribution>>(),
                GetService<IAsyncCommandExceptionHandler>());
            GetService<ThemeService>().RegisterWindow(_window);
            GetService<StoragePickerService>().RegisterWindow(_window);
            GetService<DialogService>().RegisterWindow(_window);
            _window.Activate();
            _ = EnsureCredentialHelperConfiguredAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogWriter.Write("SimpleGit11-startup.log", exception);
            throw;
        }
    }

    public static T GetService<T>()
        where T : notnull
    {
        if (Current is not App app)
        {
            throw new InvalidOperationException("The current application is not initialized.");
        }

        return app._services.GetRequiredService<T>();
    }

    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();

        services.AddSingleton<GitCommandRunner>();
        services.AddSingleton<LocalRepositoryFileSystem>();
        services.AddSingleton<LocalRepositoryPathService>();
        services.AddSingleton<LocalRepositoryFileTransfer>();
        services.AddSingleton<LocalExecutionRuntime>();
        services.AddSingleton<IExecutionProvider, LocalExecutionProvider>();
        services.AddSingleton<IExecutionProviderRegistry, ExecutionProviderRegistry>();
        services.AddSingleton<ExecutionContextService>();
        services.AddSingleton<IExecutionContextService>(provider =>
            provider.GetRequiredService<ExecutionContextService>());
        services.AddSingleton<IGitCommandRunner, ContextualGitCommandRunner>();
        services.AddSingleton<IGitRepositoryChangeDetector, GitRepositoryChangeDetector>();
        services.AddSingleton(static _ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        });
        services.AddSingleton<IProductInfoService, ProductInfoService>();
        services.AddSingleton<ILocalSettingsStore, JsonLocalSettingsStore>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IThemeService>(provider => provider.GetRequiredService<ThemeService>());
        services.AddSingleton<IAsyncCommandExceptionHandler, AsyncCommandExceptionHandler>();
        services.AddSingleton<IAsyncCommandExecutor, AsyncCommandExecutor>();
        services.AddSingleton<IMessenger, WeakReferenceMessenger>();
        services.AddSingleton<StoragePickerService>();
        services.AddSingleton<IStoragePickerService>(provider => provider.GetRequiredService<StoragePickerService>());
        services.AddSingleton<IGitRepositoryDiscoveryService, RepositoryDiscoveryService>();
        services.AddSingleton<IExecutionRepositoryDiscoveryService, ExecutionRepositoryDiscoveryService>();
        services.AddSingleton<IGitRepositoryOperationService, GitRepositoryOperationService>();
        services.AddSingleton<IGitRepositorySearchService, RepositorySearchService>();
        services.AddSingleton<IRecentRepositoriesService, RecentRepositoriesService>();
        services.AddSingleton<IGitStatusService, GitStatusService>();
        services.AddSingleton<IGitSubmoduleService, GitSubmoduleService>();
        services.AddSingleton<IGitDiffService, GitDiffService>();
        services.AddSingleton<IGitOperationQueue, GitOperationQueue>();
        services.AddSingleton<IGitStagingService, GitStagingService>();
        services.AddSingleton<IGitIgnoreService, GitIgnoreService>();
        services.AddSingleton<IGitCommitService, GitCommitService>();
        services.AddSingleton<IGitCommitWorkflowService, GitCommitWorkflowService>();
        services.AddSingleton<IGitHistoryService, GitHistoryService>();
        services.AddSingleton<IGitBranchService, GitBranchService>();
        services.AddSingleton<IGitTagService, GitTagService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IPluginDialogHost>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IGitRemoteService, GitRemoteService>();
        services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
        services.AddSingleton<IGitRevisionService, GitRevisionService>();
        services.AddSingleton<IGitReferenceDetailsService, GitReferenceDetailsService>();
        services.AddSingleton<IGitConfigService, GitConfigService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<ITextFileService, TextFileService>();
        services.AddSingleton<IGitChangeRecoveryService, GitChangeRecoveryService>();
        services.AddSingleton<IGitStashService, GitStashService>();
        services.AddSingleton<IGitRepositoryRepairService, GitRepositoryRepairService>();
        services.AddSingleton<IGitArchiveService, GitArchiveService>();
        services.AddSingleton<IGitService, GitService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ConflictEditorViewModel>();
        services.AddSingleton<RepositoryViewModel>();
        services.AddSingleton<ChangesViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<BranchesViewModel>();
        services.AddSingleton<SynchronizationViewModel>();
        services.AddTransient<CommitRangeViewModel>();
        services.AddTransient<CommitDialogViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutDialogViewModel>();

        PluginCatalog pluginCatalog = new PluginLoader(new PluginAssemblyActivator()).Load(
            services,
            Path.Combine(AppContext.BaseDirectory, "Plugins"),
            typeof(App).Assembly.GetName().Version ?? new Version(1, 0));
        services.AddSingleton<IPluginCatalog>(pluginCatalog);
        if (pluginCatalog.Failures.Count > 0)
        {
            string failureDetails = string.Join(
                Environment.NewLine,
                pluginCatalog.Failures.Select(static failure =>
                    $"{failure.PluginDirectory}: {failure.Message}"));
            ExceptionLogWriter.Write(
                "SimpleGit11-plugins.log",
                new InvalidOperationException(
                    $"One or more plugins could not be loaded.{Environment.NewLine}{failureDetails}"));
        }

        return services.BuildServiceProvider();
    }

    private void ApplyStartupTheme()
    {
        RequestedTheme = GetService<IThemeService>().CurrentTheme switch
        {
            AppThemeMode.Light => ApplicationTheme.Light,
            AppThemeMode.Dark => ApplicationTheme.Dark,
            _ => RequestedTheme
        };
    }

    private static async Task EnsureCredentialHelperConfiguredAsync()
    {
        try
        {
            IGitConfigService gitConfigService = GetService<IGitConfigService>();
            if (await gitConfigService.IsGlobalCredentialHelperManagerConfiguredAsync())
            {
                return;
            }

            ILocalizationService localizationService = GetService<ILocalizationService>();
            bool confirmed = await GetService<IDialogService>().ConfirmAsync(
                localizationService.GetString("CredentialHelperSetupDialogTitle"),
                localizationService.GetString("CredentialHelperSetupDialogMessage"),
                localizationService.GetString("CredentialHelperSetupDialogPrimaryButton"));

            if (confirmed)
            {
                await gitConfigService.SetGlobalCredentialHelperManagerAsync();
            }
        }
        catch (Exception)
        {
        }
    }
}
