using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Extensibility.Presentation;

namespace SimpleGit11.Presentation.Navigation;

internal sealed class PluginMenuItem : IDisposable
{
    private readonly IMainMenuContribution _contribution;
    private readonly Action<Action> _dispatch;
    private ICommand? _command;
    private bool _isExecuting;
    private bool _disposed;

    public PluginMenuItem(IMainMenuContribution contribution, Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(dispatch);
        _contribution = contribution;
        _dispatch = dispatch;
        Id = contribution.Id;
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        Placement = contribution.Placement;
        if (!Enum.IsDefined(Placement))
        {
            throw new ArgumentException("Unknown plugin menu placement.", nameof(contribution));
        }

        State = ReadState(out ICommand command);
        SetCommand(command);
        _contribution.PropertyChanged += Contribution_PropertyChanged;
    }

    public string Id { get; }

    public string AutomationId => $"PluginMenu.{Id}";

    public MainMenuPlacement Placement { get; }

    public PluginMenuItemState State { get; private set; }

    public event EventHandler? StateChanged;

    public async Task InvokeAsync()
    {
        if (_disposed || _isExecuting)
        {
            return;
        }

        // Reserve execution before calling plugin code, including CanExecute.
        _isExecuting = true;
        try
        {
            ICommand command = _contribution.Command;
            if (!command.CanExecute(null))
            {
                return;
            }

            QueueRefresh();
            if (command is IAsyncRelayCommand asyncCommand)
            {
                await asyncCommand.ExecuteAsync(null);
            }
            else
            {
                command.Execute(null);
            }
        }
        finally
        {
            _isExecuting = false;
            QueueRefresh();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _contribution.PropertyChanged -= Contribution_PropertyChanged;
        if (_command is not null)
        {
            _command.CanExecuteChanged -= Command_CanExecuteChanged;
        }
        StateChanged = null;
    }

    private PluginMenuItemState ReadState(out ICommand command)
    {
        string label = _contribution.Label;
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        command = _contribution.Command
            ?? throw new InvalidOperationException("Plugin menu command is required.");
        MainMenuIndicator indicator = _contribution.Indicator ?? MainMenuIndicator.None;
        if (!Enum.IsDefined(indicator.Kind)
            || (indicator.Kind != MainMenuIndicatorKind.None && string.IsNullOrWhiteSpace(indicator.AccessibleText)))
        {
            throw new InvalidOperationException("Plugin menu indicators require a known kind and a localized accessible description.");
        }
        return new PluginMenuItemState(
            label, _contribution.IconGlyph ?? string.Empty, indicator,
            !_isExecuting && command.CanExecute(null));
    }

    private void SetCommand(ICommand command)
    {
        if (ReferenceEquals(command, _command))
        {
            return;
        }

        if (_command is not null)
        {
            _command.CanExecuteChanged -= Command_CanExecuteChanged;
        }
        _command = command;
        _command.CanExecuteChanged += Command_CanExecuteChanged;
    }

    private void Contribution_PropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    private void Command_CanExecuteChanged(object? sender, EventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (!_disposed)
        {
            _dispatch(() =>
            {
                if (_disposed)
                {
                    return;
                }

                PluginMenuItemState next = ReadState(out ICommand command);
                SetCommand(command);
                if (State != next)
                {
                    State = next;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }
    }
}
