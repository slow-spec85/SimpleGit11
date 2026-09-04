using System;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Plugin.Ssh.Services;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Presentation;

internal sealed class SshMainMenuContribution : ObservableObject, IMainMenuContribution, IDisposable
{
    private readonly SshConnectionController _controller;
    private readonly IExecutionContextService _contexts;
    private readonly ISshLocalizationService _localization;
    private readonly AsyncRelayCommand _command;
    private bool _disposed;

    public SshMainMenuContribution(
        SshConnectionController controller,
        IExecutionContextService contexts,
        ISshLocalizationService localization)
    {
        _controller = controller;
        _contexts = contexts;
        _localization = localization;
        _command = new AsyncRelayCommand(controller.ToggleAsync, CanExecute);
        _controller.PropertyChanged += Controller_PropertyChanged;
        _contexts.CurrentChanged += Contexts_CurrentChanged;
    }

    public string Id => "ssh.connection";
    public string Label => _localization.GetString("SshConnectionNavigationItem");
    public string IconGlyph => "\uE839";
    public MainMenuPlacement Placement => MainMenuPlacement.Footer;
    public ICommand Command => _command;

    public MainMenuIndicator Indicator => _controller.IsBusy
        ? new(MainMenuIndicatorKind.Progress, _localization.GetString("SshConnectionBusyStatus"))
        : !_contexts.Current.IsLocal && _contexts.Current.ProviderId == SshPlugin.ProviderId
            ? new(MainMenuIndicatorKind.Success, string.Format(
                _localization.GetString("SshConnectionActiveStatusFormat"), _contexts.Current.DisplayMachineName))
            : new(MainMenuIndicatorKind.None, _localization.GetString("SshConnectionInactiveStatus"));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.PropertyChanged -= Controller_PropertyChanged;
        _contexts.CurrentChanged -= Contexts_CurrentChanged;
        _command.NotifyCanExecuteChanged();
    }

    private bool CanExecute() => !_disposed && !_controller.IsBusy
        && (_contexts.Current.IsLocal || _contexts.Current.ProviderId == SshPlugin.ProviderId);

    private void Controller_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void Contexts_CurrentChanged(object? sender, ExecutionContextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(Indicator));
        _command.NotifyCanExecuteChanged();
    }
}
