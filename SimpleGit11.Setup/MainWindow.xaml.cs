using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WixToolset.BootstrapperApplicationApi;

namespace SimpleGit11.Setup;

public sealed partial class MainWindow : Window
{
    private readonly SetupBootstrapperApplication _bootstrapper;
    private readonly Stack<WizardStep> _backStack = new();
    private WizardStep _step = WizardStep.Detecting;
    private LaunchAction _operation = LaunchAction.Install;
    private bool _existingInstallation;
    private bool _busy;
    private bool _cancelRequested;
    private int _resultStatus;
    private string? _resultError;

    internal MainWindow(SetupBootstrapperApplication bootstrapper)
    {
        _bootstrapper = bootstrapper;
        InitializeComponent();
        int languageIndex = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? 1 : 0;
        LanguageSelector.SelectedIndex = languageIndex;
        ApplyLanguage();
        ShowStep(WizardStep.Detecting);
        AppWindow.Closing += OnWindowClosing;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        // The scope page needs the most space; 448 DIP also leaves room for
        // wrapped headings, the common header and navigation at the default text size.
        AppWindow.Resize(new SizeInt32((int)(720 * scale), (int)(448 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
    }

    internal void ShowDetectedState(
        bool currentPackageInstalled,
        bool existingInstallation,
        bool machineScope,
        string? folder,
        bool sshInstalled,
        bool desktopInstalled,
        LaunchAction requestedAction)
    {
        _existingInstallation = existingInstallation;
        MachineScope.IsChecked = machineScope;
        UserScope.IsChecked = !machineScope;
        FolderPath.Text = folder ?? GetDefaultFolder(machineScope);
        SshFeature.IsChecked = sshInstalled;
        DesktopFeature.IsChecked = desktopInstalled;
        PurgeData.IsEnabled = !machineScope;
        _backStack.Clear();

        _operation = requestedAction is LaunchAction.Uninstall or LaunchAction.Repair or LaunchAction.Modify
            ? requestedAction : LaunchAction.Install;
        ShowStep(GetInitialStep(currentPackageInstalled, requestedAction));
    }

    internal void SetProgress(int percent)
    {
        InstallProgress.Value = Math.Clamp(percent, 0, 100);
        ProgressPercent.Text = $"{percent}%";
    }

    internal void FinishOperation(int status, string? error)
    {
        _busy = false;
        _resultStatus = status;
        _resultError = error;
        ShowStep(WizardStep.Result);
    }

    private void NextButton_Click(object sender, RoutedEventArgs args)
    {
        switch (_step)
        {
            case WizardStep.Welcome:
                NavigateTo(GetWelcomeNextStep(_existingInstallation));
                break;
            case WizardStep.Scope:
                FolderPath.Text = GetDefaultFolder(MachineScope.IsChecked == true);
                NavigateTo(WizardStep.Folder);
                break;
            case WizardStep.Folder:
                if (!TryNormalizeFolder(out string? folder))
                {
                    FolderValidation.Visibility = Visibility.Visible;
                    return;
                }

                FolderPath.Text = folder;
                FolderValidation.Visibility = Visibility.Collapsed;
                NavigateTo(WizardStep.Features);
                break;
            case WizardStep.Features:
            case WizardStep.Purge:
                NavigateTo(WizardStep.Ready);
                break;
            case WizardStep.Ready:
                BeginOperation();
                break;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs args)
    {
        if (_backStack.Count > 0)
        {
            ShowStep(_backStack.Pop());
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs args)
    {
        if (_busy)
        {
            _cancelRequested = true;
            _bootstrapper.RequestCancel();
            CancelButton.IsEnabled = false;
            ProgressText.Text = Text("Cancelling");
            return;
        }

        Close();
    }

    private void ChangeButton_Click(object sender, RoutedEventArgs args)
    {
        _operation = LaunchAction.Modify;
        NavigateTo(WizardStep.Features);
    }

    private void RepairButton_Click(object sender, RoutedEventArgs args)
    {
        _operation = LaunchAction.Repair;
        NavigateTo(WizardStep.Ready);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs args)
    {
        _operation = LaunchAction.Uninstall;
        PurgeData.IsChecked = false;
        NavigateTo(WizardStep.Purge);
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs args)
    {
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            FolderPath.Text = folder.Path;
            FolderValidation.Visibility = Visibility.Collapsed;
        }
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        ApplicationLanguages.PrimaryLanguageOverride = LanguageSelector.SelectedIndex == 1 ? "ru-RU" : "en-US";
        if (DetectingText is not null)
        {
            ApplyLanguage();
        }
    }

    private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_busy)
        {
            args.Cancel = true;
            _cancelRequested = true;
            _bootstrapper.RequestCancel();
        }
        else if (_step != WizardStep.Result)
        {
            _bootstrapper.CancelBeforeOperation();
        }
    }

    private void BeginOperation()
    {
        string? folder = null;
        if (!_existingInstallation && _operation == LaunchAction.Install)
        {
            if (!TryNormalizeFolder(out folder))
            {
                ShowStep(WizardStep.Folder);
                FolderValidation.Visibility = Visibility.Visible;
                return;
            }
        }

        _busy = true;
        _cancelRequested = false;
        CancelButton.IsEnabled = true;
        ShowStep(WizardStep.Progress);
        _bootstrapper.StartOperation(
            _operation,
            MachineScope.IsChecked == true ? BundleScope.PerMachine : BundleScope.PerUser,
            folder,
            SshFeature.IsChecked == true,
            DesktopFeature.IsChecked == true,
            _operation == LaunchAction.Uninstall && PurgeData.IsChecked == true);
    }

    private bool TryNormalizeFolder(out string? folder)
    {
        folder = null;
        try
        {
            string candidate = FolderPath.Text.Trim();
            if (!Path.IsPathFullyQualified(candidate))
            {
                return false;
            }

            folder = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void NavigateTo(WizardStep next)
    {
        _backStack.Push(_step);
        ShowStep(next);
    }

    private void ShowStep(WizardStep step)
    {
        _step = step;
        DetectingPanel.Visibility = step == WizardStep.Detecting ? Visibility.Visible : Visibility.Collapsed;
        WelcomePanel.Visibility = step == WizardStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        ScopePanel.Visibility = step == WizardStep.Scope ? Visibility.Visible : Visibility.Collapsed;
        FolderPanel.Visibility = step == WizardStep.Folder ? Visibility.Visible : Visibility.Collapsed;
        FeaturesPanel.Visibility = step == WizardStep.Features ? Visibility.Visible : Visibility.Collapsed;
        MaintenancePanel.Visibility = step == WizardStep.Maintenance ? Visibility.Visible : Visibility.Collapsed;
        PurgePanel.Visibility = step == WizardStep.Purge ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = step == WizardStep.Ready ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = step == WizardStep.Progress ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = step == WizardStep.Result ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _backStack.Count > 0 && step is not WizardStep.Progress and not WizardStep.Result
            ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = step is WizardStep.Welcome or WizardStep.Scope or WizardStep.Folder
            or WizardStep.Features or WizardStep.Purge or WizardStep.Ready ? Visibility.Visible : Visibility.Collapsed;
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Title = Text("WindowTitle");
        PageTitle.Text = _step switch
        {
            WizardStep.Detecting => Text("DetectingTitle"),
            WizardStep.Welcome => Text(_existingInstallation ? "UpdateWelcomeTitle" : "WelcomeTitle"),
            WizardStep.Scope => Text("ScopeTitle"),
            WizardStep.Folder => Text("FolderTitle"),
            WizardStep.Features => Text("FeaturesTitle"),
            WizardStep.Maintenance => Text("MaintenanceTitle"),
            WizardStep.Purge => Text("PurgeTitle"),
            WizardStep.Ready => Text("ReadyTitle"),
            WizardStep.Progress => Text("ProgressTitle"),
            _ => Text("ResultTitle")
        };
        DetectingText.Text = Text("DetectingText");
        WelcomeText.Text = Text(_existingInstallation ? "UpdateWelcomeText" : "WelcomeText");
        WelcomeNote.Text = Text(_existingInstallation ? "UpdateWelcomeNote" : "WelcomeNote");
        ScopeText.Text = Text("ScopeText");
        UserScope.Content = Text("UserScope");
        MachineScope.Content = Text("MachineScope");
        MachineScopeNote.Text = Text("MachineScopeNote");
        FolderText.Text = Text("FolderText");
        FolderPath.Header = Text("FolderLabel");
        BrowseButton.Content = Text("Browse");
        FolderValidation.Text = Text("InvalidFolder");
        FeaturesText.Text = Text("FeaturesText");
        SshFeature.Content = Text("SshFeature");
        DesktopFeature.Content = Text("DesktopFeature");
        MaintenanceText.Text = Text("MaintenanceText");
        ChangeButton.Content = Text("Change");
        RepairButton.Content = Text("Repair");
        RemoveButton.Content = Text("Remove");
        PurgeText.Text = Text("PurgeText");
        PurgeData.Content = Text("PurgeData");
        PurgeWarning.Text = Text("PurgeWarning");
        ReadyText.Text = Text("ReadyText");
        ReadySummary.Text = GetReadySummary();
        ProgressText.Text = Text(_cancelRequested ? "Cancelling" : "ProgressText");
        ResultText.Text = Text(_resultStatus == 0 ? "SuccessText" : "FailureText");
        ResultDetails.Text = _resultStatus == 0 ? string.Empty : _resultError ?? $"0x{_resultStatus:X8}";
        CancelButton.Content = Text(_step == WizardStep.Result ? "Close" : "Cancel");
        BackButton.Content = Text("Back");
        NextButton.Content = Text(_step == WizardStep.Ready ? OperationKey() : "Next");
        AutomationProperties.SetName(LanguageSelector, Text("LanguageLabel"));
        ((ComboBoxItem)LanguageSelector.Items[0]).Content = Text("EnglishLanguage");
        ((ComboBoxItem)LanguageSelector.Items[1]).Content = Text("RussianLanguage");
    }

    private string GetReadySummary()
    {
        if (_operation == LaunchAction.Uninstall)
        {
            return PurgeData.IsChecked == true ? Text("SummaryRemoveAndPurge") : Text("SummaryRemove");
        }

        if (_operation == LaunchAction.Repair)
        {
            return Text("SummaryRepair");
        }

        if (_operation == LaunchAction.Modify)
        {
            return Text("SummaryChange");
        }

        return _existingInstallation
            ? Text("SummaryUpdate")
            : string.Format(CultureInfo.CurrentUICulture, Text("SummaryInstall"), FolderPath.Text);
    }

    private string OperationKey()
    {
        return _operation switch
        {
            LaunchAction.Uninstall => "Remove",
            LaunchAction.Repair => "Repair",
            LaunchAction.Modify => "Change",
            _ => _existingInstallation ? "Update" : "Install"
        };
    }

    private static WizardStep GetInitialStep(bool currentPackageInstalled, LaunchAction requestedAction)
    {
        return requestedAction switch
        {
            LaunchAction.Uninstall => WizardStep.Purge,
            LaunchAction.Repair => WizardStep.Ready,
            LaunchAction.Modify => WizardStep.Features,
            _ => currentPackageInstalled ? WizardStep.Maintenance : WizardStep.Welcome
        };
    }

    private static WizardStep GetWelcomeNextStep(bool existingInstallation)
    {
        return existingInstallation ? WizardStep.Ready : WizardStep.Scope;
    }

    private static string GetDefaultFolder(bool machineScope)
    {
        string root = machineScope
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        return Path.Combine(root, "SimpleGit11");
    }

    private static string Text(string key)
    {
        string value = new ResourceLoader().GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private enum WizardStep
    {
        Detecting,
        Welcome,
        Scope,
        Folder,
        Features,
        Maintenance,
        Purge,
        Ready,
        Progress,
        Result
    }
}
