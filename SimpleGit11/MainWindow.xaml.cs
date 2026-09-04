using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensions;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Models;
using SimpleGit11.Pages;
using SimpleGit11.Presentation.Execution;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.Presentation.Theming;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Services.Execution;
using SimpleGit11.ViewModels;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SimpleGit11;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double ShortWindowHeight = 760;
    private const double TallWindowHeight = 820;
    private const double OneRecentRepositoryHeight = 56;
    private const double TwoRecentRepositoriesHeight = 112;
    private const double ThreeRecentRepositoriesHeight = 168;
    private const double RepositoryMenuMaxHeight = 480;
    private static readonly TimeSpan MinimumWindowInactiveDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AutomaticRefreshCooldown = TimeSpan.FromSeconds(10);
    private readonly ILocalizationService _localizationService;
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly IDialogService _dialogService;
    private readonly ExecutionContextShellCoordinator _executionContextCoordinator;
    private readonly IGitRepositoryChangeDetector _gitRepositoryChangeDetector;
    private readonly IGitService _gitService;
    private readonly PluginMenuHost _pluginMenuHost;
    private readonly WindowActivationRefreshGate _activationRefreshGate = new(
        MinimumWindowInactiveDuration,
        AutomaticRefreshCooldown);
    private readonly SemaphoreSlim _pageRefreshSemaphore = new(1, 1);
    private bool _allowClose;
    private bool _isCloseDialogOpen;
    private bool _isRefreshingCurrentPage;
    private bool _isChangingExecutionContext;
    private bool _isSynchronizingNavigationSelection;
    private long _latestPageRefreshRequest;

    public MainWindowViewModel ViewModel { get; }

    public RepositoryViewModel RepositoryViewModel { get; }

    public BranchesViewModel BranchesViewModel { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        ILocalizationService localizationService,
        IAsyncCommandExecutor asyncCommandExecutor,
        IDialogService dialogService,
        IExecutionContextService executionContextService,
        IGitRepositoryChangeDetector gitRepositoryChangeDetector,
        IGitService gitService,
        IMessenger messenger,
        IEnumerable<IMainMenuContribution> menuContributions,
        IAsyncCommandExceptionHandler exceptionHandler)
    {
        ViewModel = viewModel;
        _localizationService = localizationService;
        _asyncCommandExecutor = asyncCommandExecutor;
        _dialogService = dialogService;
        _gitRepositoryChangeDetector = gitRepositoryChangeDetector;
        _gitService = gitService;
        RepositoryViewModel = App.GetService<RepositoryViewModel>();
        BranchesViewModel = App.GetService<BranchesViewModel>();
        InitializeComponent();
        _pluginMenuHost = new PluginMenuHost(
            ShellNavigation, menuContributions, asyncCommandExecutor, exceptionHandler);
        Closed += MainWindow_Closed;
        _executionContextCoordinator = new ExecutionContextShellCoordinator(
            executionContextService, DispatchExecutionContextAction, asyncCommandExecutor,
            ResetRepositoryForExecutionContext, () => RefreshCurrentPageAsync(),
            messenger, localizationService);
        ConfigureTitleBar();
        AppWindow.Closing += AppWindow_Closing;
        SizeChanged += MainWindow_SizeChanged;
        UpdateRecentRepositoriesMaxHeight(Bounds.Height);
        RootLayout.ActualThemeChanged += RootLayout_ActualThemeChanged;
        ShellNavigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, OnPaneOpenChanged);
        UpdatePaneFooterVisibility();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.NavigationRequested += ViewModel_NavigationRequested;
        Activated += MainWindow_Activated;
        NavigateToTopLevelPage(typeof(RepositoryPage));
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _executionContextCoordinator.Dispose();
        _pluginMenuHost.Dispose();
    }

    private void DispatchExecutionContextAction(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ = DispatcherQueue.TryEnqueue(() => action());
        }
    }

    private void ResetRepositoryForExecutionContext()
    {
        _isChangingExecutionContext = true;
        try
        {
            RepositoryViewModel.CloseForExecutionContextChange();
            ViewModel.RefreshRecentRepositoriesForExecutionContext();
        }
        finally
        {
            _isChangingExecutionContext = false;
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_activationRefreshGate.OnActivationChanged(isActive))
        {
            _ = _asyncCommandExecutor.ExecuteAsync(
                RefreshCurrentPageAfterWindowActivationAsync);
        }
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        UpdateRecentRepositoriesMaxHeight(e.Size.Height);
    }

    private void UpdateRecentRepositoriesMaxHeight(double windowHeight)
    {
        RecentRepositoriesList.MaxHeight = windowHeight switch
        {
            < ShortWindowHeight => OneRecentRepositoryHeight,
            < TallWindowHeight => TwoRecentRepositoriesHeight,
            _ => ThreeRecentRepositoriesHeight
        };
    }

    private async Task RefreshCurrentPageAfterWindowActivationAsync()
    {
        RepositoryInfo? repository = ViewModel.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        bool hasChanged;
        try
        {
            hasChanged = await _gitRepositoryChangeDetector.HasChangedAsync(repository);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            hasChanged = true;
        }

        if (!hasChanged
            || !string.Equals(
                ViewModel.CurrentRepository?.Path,
                repository.Path,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RepositoryViewModel.RefreshCurrentRepositoryIdentityAsync();
        await RefreshCurrentPageAsync(
            refreshRepositoryIdentity: true,
            repositoryIdentityAlreadyRefreshed: true);
    }

    private async void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (_allowClose || ContentFrame.CurrentSourcePageType != typeof(CommitRangePage))
        {
            return;
        }

        args.Cancel = true;
        if (_isCloseDialogOpen)
        {
            return;
        }

        _isCloseDialogOpen = true;
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                RequestedTheme = RootLayout.ActualTheme,
                Title = _localizationService.GetString("CloseApplicationConfirmation"),
                PrimaryButtonText = _localizationService.GetString("CloseApplicationYesButton"),
                CloseButtonText = _localizationService.GetString("CloseApplicationNoButton"),
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _allowClose = true;
                Close();
            }
        }
        finally
        {
            _isCloseDialogOpen = false;
        }
    }

    private void ViewModel_NavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        if (e.Target == AppNavigationTarget.CommitRange)
        {
            ContentFrame.Navigate(typeof(CommitRangePage), e.Parameter);
            UpdateBackNavigationState();
            return;
        }

        Type pageType = GetPageType(e.Target);
        bool refreshChangesPage = e.Target == AppNavigationTarget.Changes
            && ContentFrame.Content is ChangesPage;
        NavigationViewItem? navigationItem = ShellNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .Where(item => item.Tag is not PluginMenuItem)
            .FirstOrDefault(item => GetPageType(item.Tag) == pageType);
        if (navigationItem is not null)
        {
            ShellNavigation.SelectedItem = navigationItem;
        }

        NavigateToTopLevelPage(pageType);
        if (refreshChangesPage)
        {
            _ = _asyncCommandExecutor.ExecuteAsync(
                () => RefreshCurrentPageAsync());
        }
    }

    private void OnPaneOpenChanged(DependencyObject sender, DependencyProperty dp)
    {
        UpdatePaneFooterVisibility();
    }

    private void UpdatePaneFooterVisibility()
    {
        PaneFooterContent.Visibility = ShellNavigation.IsPaneOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isChangingExecutionContext)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentRepository))
        {
            RepositoryInfo? repository = ViewModel.CurrentRepository;
            if (repository is not null)
            {
                try
                {
                    await _gitRepositoryChangeDetector.EnsureBaselineAsync(repository);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(exception);
                }
            }

            await ViewModel.RefreshRemotesAsync();
            await RefreshCurrentPageAsync();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.CurrentBranch)
            && ContentFrame.Content is not BranchesPage
            && !_isRefreshingCurrentPage)
        {
            await RefreshCurrentPageAsync();
        }
    }

    private void RepositoryPaneHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        WorktreeViewItem[] worktrees = RepositoryViewModel.Worktrees.ToArray();
        RepositoryInfo[] repositories = RepositoryViewModel.FoundRepositories.ToArray();
        if (worktrees.Length == 0 && repositories.Length == 0)
        {
            return;
        }

        MenuFlyout flyout = new()
        {
            MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter))
            {
                Setters =
                {
                    new Setter(FrameworkElement.MaxHeightProperty, RepositoryMenuMaxHeight)
                }
            }
        };

        foreach (WorktreeViewItem worktree in worktrees)
        {
            MenuFlyoutItem item = new()
            {
                Text = $"{worktree.Name} — {worktree.ReferenceText}",
                Tag = worktree,
                IsEnabled = worktree.CanOpen,
                Icon = worktree.Worktree.IsCurrent ? new SymbolIcon(Symbol.Accept) : null
            };
            ToolTipService.SetToolTip(
                item,
                string.Format(_localizationService.GetString("WorktreeMenuItemToolTip"), worktree.Path));
            item.Click += WorktreeMenuFlyoutItem_Click;
            flyout.Items.Add(item);
        }

        if (worktrees.Length > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (RepositoryInfo repository in repositories)
        {
            MenuFlyoutItem item = new()
            {
                Text = repository.Name,
                Tag = repository
            };
            ToolTipService.SetToolTip(
                item,
                string.Format(_localizationService.GetString("RepositoryMenuItemToolTip"), repository.Path));
            item.Click += FoundRepositoryMenuFlyoutItem_Click;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(RepositoryPaneHeaderButton);
    }

    private async void RemotePaneHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentRepository is null)
        {
            return;
        }

        await ViewModel.RefreshRemotesAsync();
        if (ViewModel.Remotes.Count == 0)
        {
            return;
        }

        MenuFlyout flyout = new();
        foreach (GitRemote remote in ViewModel.Remotes)
        {
            bool isSelected = string.Equals(remote.Name, ViewModel.SelectedRemoteName, StringComparison.Ordinal);
            MenuFlyoutItem item = new()
            {
                Text = remote.Name,
                Tag = remote,
                IsEnabled = !isSelected,
                Icon = isSelected ? new SymbolIcon(Symbol.Accept) : null
            };
            ToolTipService.SetToolTip(item, remote.DisplayUrl);
            item.Click += RemoteMenuFlyoutItem_Click;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(RemotePaneHeaderButton);
    }

    private void WorktreeMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorktreeViewItem worktree }
            && worktree.OpenCommand.CanExecute(null))
        {
            worktree.OpenCommand.TryExecute();
        }
    }

    private async void RemoteMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: GitRemote remote }
            || string.Equals(remote.Name, ViewModel.SelectedRemoteName, StringComparison.Ordinal))
        {
            return;
        }

        ViewModel.SelectRemote(remote.Name);
        await RefreshCurrentPageFromSelectedRemoteAsync();
    }

    private async void BranchPaneHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentRepository is null)
        {
            return;
        }

        GitBranch[] branches = BranchesViewModel.Branches.ToArray();
        if (branches.Length == 0)
        {
            await BranchesViewModel.RefreshBranchesLocalAsync();
            branches = BranchesViewModel.Branches.ToArray();
        }

        if (branches.Length == 0)
        {
            return;
        }

        MenuFlyout flyout = new();
        foreach (GitBranch branch in branches)
        {
            MenuFlyoutItem item = new()
            {
                Text = branch.Name,
                Tag = branch,
                IsEnabled = !branch.IsCurrent,
                Icon = branch.IsCurrent ? new SymbolIcon(Symbol.Accept) : null
            };
            item.Click += LocalBranchMenuFlyoutItem_Click;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(BranchPaneHeaderButton);
    }

    private void FoundRepositoryMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: RepositoryInfo repository }
            && RepositoryViewModel.OpenFoundRepositoryCommand.CanExecute(repository))
        {
            RepositoryViewModel.OpenFoundRepositoryCommand.TryExecute(repository);
        }
    }

    private void LocalBranchMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: GitBranch branch }
            && BranchesViewModel.CheckoutContextBranchCommand.CanExecute(branch))
        {
            BranchesViewModel.CheckoutContextBranchCommand.TryExecute(branch);
        }
    }

    private async Task RefreshCurrentPageAsync(
        bool refreshRepositoryIdentity = false,
        bool repositoryIdentityAlreadyRefreshed = false)
    {
        object? requestedPage = ContentFrame.Content;
        long requestId = Interlocked.Increment(ref _latestPageRefreshRequest);
        await _pageRefreshSemaphore.WaitAsync();
        try
        {
            if (requestId != Volatile.Read(ref _latestPageRefreshRequest)
                || !ReferenceEquals(requestedPage, ContentFrame.Content)
                || requestedPage is not IPageRefreshTarget refreshTarget)
            {
                return;
            }

            _isRefreshingCurrentPage = true;
            if (refreshRepositoryIdentity
                && !repositoryIdentityAlreadyRefreshed
                && requestedPage is not RepositoryPage)
            {
                await RepositoryViewModel.RefreshCurrentRepositoryIdentityAsync();
            }

            await refreshTarget.RefreshAsync();
        }
        finally
        {
            _isRefreshingCurrentPage = false;
            _pageRefreshSemaphore.Release();
        }
    }

    private Task RefreshCurrentPageFromSelectedRemoteAsync()
    {
        return ContentFrame.Content is IRemoteSelectionRefreshPage refreshPage
            ? refreshPage.RefreshSelectedRemoteAsync()
            : Task.CompletedTask;
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        UpdateTitleBarButtonColors();

        // Установка иконки в заголовке окна
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(wndId);

            appWindow.SetIcon(@"Assets\AppIcon.ico");
        }
    }

    private void RootLayout_ActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTitleBarButtonColors();
    }

    private void UpdateTitleBarButtonColors()
    {
        var foreground = ThemeResourceResolver.GetColor("TitleBarButtonForegroundBrush");
        var inactiveForeground = ThemeResourceResolver.GetColor("TitleBarButtonInactiveForegroundBrush");
        var hoverBackground = ThemeResourceResolver.GetColor("TitleBarButtonHoverBackgroundBrush");
        var pressedBackground = ThemeResourceResolver.GetColor("TitleBarButtonPressedBackgroundBrush");

        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonPressedForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = inactiveForeground;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBackground;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = pressedBackground;
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSynchronizingNavigationSelection)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            NavigateToTopLevelPage(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is not NavigationViewItem selectedItem)
        {
            return;
        }

        if (selectedItem.Tag is PluginMenuItem)
        {
            return;
        }

        NavigateToTopLevelPage(GetPageType(selectedItem.Tag));
    }

    private void ShellNavigation_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (_pluginMenuHost.TryInvoke(args.InvokedItemContainer))
        {
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem navigationItem
            && string.Equals(navigationItem.Tag as string, "About", StringComparison.Ordinal))
        {
            _ = _asyncCommandExecutor.ExecuteAsync(_dialogService.ShowAboutAsync);
        }
    }

    private static Type GetPageType(object? navigationTag)
    {
        return navigationTag switch
        {
            "Repository" => typeof(RepositoryPage),
            "Changes" => typeof(ChangesPage),
            "History" => typeof(HistoryPage),
            "Branches" => typeof(BranchesPage),
            "Synchronization" => typeof(SynchronizationPage),
            _ => typeof(RepositoryPage)
        };
    }

    private static Type GetPageType(AppNavigationTarget navigationTarget)
    {
        return navigationTarget switch
        {
            AppNavigationTarget.Changes => typeof(ChangesPage),
            AppNavigationTarget.History => typeof(HistoryPage),
            AppNavigationTarget.Branches => typeof(BranchesPage),
            AppNavigationTarget.Synchronization => typeof(SynchronizationPage),
            _ => typeof(RepositoryPage)
        };
    }

    private void NavigateToTopLevelPage(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }

        ContentFrame.BackStack.Clear();
        UpdateBackNavigationState();
    }

    private void ShellNavigation_BackRequested(
        NavigationView sender,
        NavigationViewBackRequestedEventArgs args)
    {
        NavigateBack();
    }

    private void BackKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ContentFrame.CanGoBack)
        {
            return;
        }

        NavigateBack();
        args.Handled = true;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        NavigationViewItem? navigationItem = ShellNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .Where(item => item.Tag is not PluginMenuItem)
            .FirstOrDefault(item => GetPageType(item.Tag) == e.SourcePageType);
        if (navigationItem is not null
            && !ReferenceEquals(ShellNavigation.SelectedItem, navigationItem))
        {
            _isSynchronizingNavigationSelection = true;
            try
            {
                ShellNavigation.SelectedItem = navigationItem;
            }
            finally
            {
                _isSynchronizingNavigationSelection = false;
            }
        }

        UpdateBackNavigationState();
        _ = _asyncCommandExecutor.ExecuteAsync(
            () => RefreshCurrentPageAsync(refreshRepositoryIdentity: true));
    }

    private void NavigateBack()
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void UpdateBackNavigationState()
    {
        ShellNavigation.IsBackEnabled = ContentFrame.CanGoBack;
    }

}
