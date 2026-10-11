using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using WixToolset.BootstrapperApplicationApi;

namespace SimpleGit11.Setup;

internal sealed class SetupBootstrapperApplication : BootstrapperApplication
{
    private const string MsiPackageId = "SimpleGit11Msi";
    private const string InstallerRegistryKey = @"Software\SimpleGit11\Installer";
    private static readonly Guid ProductUpgradeCode = new("2DBB44F0-1B39-4A09-9D63-52DFDDC73441");
    private readonly HashSet<string> _bundlesToRemove = new(StringComparer.OrdinalIgnoreCase);

    private MainWindow? _window;
    private IBootstrapperCommand? _command;
    private IntPtr _windowHandle;
    private bool _packageInstalled;
    private bool _relatedMsiInstalled;
    private bool _machineInstallation;
    private bool _sshInstalled;
    private bool _desktopInstalled;
    private bool _sshSelected;
    private bool _desktopSelected;
    private bool _cancelRequested;
    private bool _msiUninstallPlanned;
    private int _exitCode;
    private LaunchAction _plannedAction;
    private string? _lastError;

    internal bool IsHeadless => _command?.Display != Display.Full;

    internal void AttachWindow(MainWindow window)
    {
        _window = window;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
    }

    internal void Detect()
    {
        engine.Detect(_windowHandle);
    }

    internal void StartOperation(
        LaunchAction action,
        BundleScope scope,
        string? folder,
        bool ssh,
        bool desktop,
        bool purgeUserData)
    {
        _plannedAction = action;
        _sshSelected = ssh;
        _desktopSelected = desktop;
        _cancelRequested = false;
        _lastError = null;
        _bundlesToRemove.Clear();
        _msiUninstallPlanned = false;
        engine.SetVariableString("SetupInstallFolder", folder ?? string.Empty, false);
        engine.SetVariableString("PurgeUserData", purgeUserData ? "1" : "0", false);
        engine.Plan(action, scope);
    }

    internal void RequestCancel()
    {
        _cancelRequested = true;
    }

    internal void CancelBeforeOperation()
    {
        _exitCode = 1602;
    }

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        _command = args.Command;
    }

    protected override void Run()
    {
        Thread uiThread = new(() => Program.StartWinUi(this));
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
        int exitCode = (_exitCode & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
            ? _exitCode & 0xFFFF
            : _exitCode;
        engine.Quit(exitCode);
    }

    protected override void OnDetectPackageComplete(DetectPackageCompleteEventArgs args)
    {
        base.OnDetectPackageComplete(args);
        if (args.PackageId == MsiPackageId)
        {
            _packageInstalled = args.State == PackageState.Present;
        }
    }

    protected override void OnDetectRelatedMsiPackage(DetectRelatedMsiPackageEventArgs args)
    {
        base.OnDetectRelatedMsiPackage(args);
        if (args.PackageId != MsiPackageId
            || !Guid.TryParse(args.UpgradeCode, out Guid upgradeCode)
            || upgradeCode != ProductUpgradeCode)
        {
            return;
        }

        _relatedMsiInstalled = true;
        _machineInstallation = args.PerMachine;
        _sshInstalled = MsiQueryFeatureState(args.ProductCode, "Ssh") == 3;
        _desktopInstalled = MsiQueryFeatureState(args.ProductCode, "Desktop") == 3;
    }

    protected override void OnDetectMsiFeature(DetectMsiFeatureEventArgs args)
    {
        base.OnDetectMsiFeature(args);
        if (args.PackageId != MsiPackageId)
        {
            return;
        }

        if (args.FeatureId == "Ssh")
        {
            _sshInstalled = args.State == FeatureState.Local;
        }
        else if (args.FeatureId == "Desktop")
        {
            _desktopInstalled = args.State == FeatureState.Local;
        }
    }

    protected override void OnDetectComplete(DetectCompleteEventArgs args)
    {
        base.OnDetectComplete(args);
        if (args.Status < 0)
        {
            _exitCode = args.Status;
            CompleteUi(args.Status, null);
            return;
        }

        string? userFolder = ReadInstallFolder(RegistryHive.CurrentUser);
        string? machineFolder = ReadInstallFolder(RegistryHive.LocalMachine);
        bool installed = _packageInstalled || _relatedMsiInstalled || userFolder is not null || machineFolder is not null;
        bool machineScope = _machineInstallation || machineFolder is not null;
        string? folder = machineScope ? machineFolder : userFolder;
        LaunchAction requestedAction = _command?.Action ?? LaunchAction.Install;
        if (IsHeadless)
        {
            BundleScope scope = _command?.Scope is BundleScope.PerMachine or BundleScope.PerUser
                ? _command.Scope
                : machineScope ? BundleScope.PerMachine : BundleScope.PerUser;
            StartOperation(requestedAction, scope, null, _sshInstalled, _desktopInstalled, false);
            return;
        }

        Dispatch(window => window.ShowDetectedState(
            _packageInstalled,
            installed,
            machineScope,
            folder,
            _sshInstalled,
            _desktopInstalled,
            requestedAction));
    }

    protected override void OnPlanRelatedBundleType(PlanRelatedBundleTypeEventArgs args)
    {
        base.OnPlanRelatedBundleType(args);
        if (_plannedAction == LaunchAction.Uninstall && _packageInstalled
            && (_command?.Relation ?? RelationType.None) == RelationType.None
            && args.RecommendedType == RelatedBundlePlanType.Upgrade)
        {
            // A rebuilt EXE has a different bundle identity but the same MSI. Treat
            // previous owners as dependents being removed: Burn then includes their
            // provider keys in the uninstall plan before checking MSI dependencies.
            args.Type = RelatedBundlePlanType.DependentAddon;
            _bundlesToRemove.Add(args.BundleCode);
        }
    }

    protected override void OnPlanRelatedBundle(PlanRelatedBundleEventArgs args)
    {
        base.OnPlanRelatedBundle(args);
        if (_bundlesToRemove.Contains(args.BundleCode))
        {
            args.State = RequestState.Absent;
        }
    }

    protected override void OnPlannedPackage(PlannedPackageEventArgs args)
    {
        base.OnPlannedPackage(args);
        if (args.PackageId == MsiPackageId)
        {
            _msiUninstallPlanned = args.Execute == ActionState.Uninstall;
        }
    }

    protected override void OnPlanMsiFeature(PlanMsiFeatureEventArgs args)
    {
        base.OnPlanMsiFeature(args);
        if (args.PackageId != MsiPackageId
            || _plannedAction is LaunchAction.Uninstall or LaunchAction.Repair)
        {
            return;
        }

        args.State = args.FeatureId switch
        {
            "Core" => FeatureState.Local,
            "Ssh" => _sshSelected ? FeatureState.Local : FeatureState.Absent,
            "Desktop" => _desktopSelected ? FeatureState.Local : FeatureState.Absent,
            _ => args.RecommendedState
        };
    }

    protected override void OnPlanComplete(PlanCompleteEventArgs args)
    {
        base.OnPlanComplete(args);
        if (args.Status < 0)
        {
            _exitCode = args.Status;
            CompleteUi(args.Status, _lastError);
            return;
        }

        if (_plannedAction == LaunchAction.Uninstall && _packageInstalled && !_msiUninstallPlanned)
        {
            _exitCode = unchecked((int)0x80070643);
            _lastError = "Windows Installer did not schedule removal of SimpleGit11. Another installer bundle may still depend on it.";
            CompleteUi(_exitCode, _lastError);
            return;
        }

        engine.Apply(_windowHandle);
    }

    protected override void OnProgress(ProgressEventArgs args)
    {
        base.OnProgress(args);
        args.Cancel = _cancelRequested;
        Dispatch(window => window.SetProgress(args.OverallPercentage));
    }

    protected override void OnExecuteProgress(ExecuteProgressEventArgs args)
    {
        base.OnExecuteProgress(args);
        args.Cancel = _cancelRequested;
        Dispatch(window => window.SetProgress(args.OverallPercentage));
    }

    protected override void OnError(WixToolset.BootstrapperApplicationApi.ErrorEventArgs args)
    {
        base.OnError(args);
        _lastError = args.ErrorMessage;
        if (_cancelRequested)
        {
            args.Result = Result.Cancel;
        }
    }

    protected override void OnApplyComplete(ApplyCompleteEventArgs args)
    {
        base.OnApplyComplete(args);
        _exitCode = args.Status;
        if (_exitCode >= 0 && _plannedAction == LaunchAction.Uninstall
            && (ReadInstallFolder(RegistryHive.CurrentUser) is not null
                || ReadInstallFolder(RegistryHive.LocalMachine) is not null))
        {
            _exitCode = unchecked((int)0x80070643);
            _lastError = "SimpleGit11 is still registered as installed after the uninstall operation.";
        }

        CompleteUi(_exitCode, _lastError);
    }

    private void CompleteUi(int status, string? error)
    {
        if (IsHeadless)
        {
            Dispatch(window => window.Close());
        }
        else
        {
            Dispatch(window => window.FinishOperation(status, error));
        }
    }

    private void Dispatch(Action<MainWindow> action)
    {
        MainWindow? window = _window;
        if (window is not null)
        {
            window.DispatcherQueue.TryEnqueue(() => action(window));
        }
    }

    private static string? ReadInstallFolder(RegistryHive hive)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using RegistryKey? key = baseKey.OpenSubKey(InstallerRegistryKey);
        return key?.GetValue("InstallFolder") as string;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiQueryFeatureStateW")]
    private static extern int MsiQueryFeatureState(string productCode, string featureId);
}
