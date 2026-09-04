using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed partial class RemoteViewItem
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly Action<RemoteViewItem> _select;
    private readonly Func<RemoteViewItem, Task> _rename;
    private readonly Func<RemoteViewItem, Task> _edit;
    private readonly Func<RemoteViewItem, Task> _remove;
    private readonly Action<string> _copy;
    private bool _isCurrent;

    public RemoteViewItem(GitRemote remote,
                            ILocalizationService localizationService,
                            IAsyncCommandExecutor asyncCommandExecutor,
                            Action<RemoteViewItem> select,
                            Func<RemoteViewItem, Task> rename,
                            Func<RemoteViewItem, Task> edit,
                            Func<RemoteViewItem, Task> remove,
                            Action<string> copy,
                            bool isCurrent)
    {
        Remote = remote;
        _asyncCommandExecutor = asyncCommandExecutor ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        _select = select ?? throw new ArgumentNullException(nameof(select));
        _rename = rename ?? throw new ArgumentNullException(nameof(edit));
        _edit = edit ?? throw new ArgumentNullException(nameof(edit));
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _isCurrent = isCurrent;
        ReferenceText = remote.DisplayUrl;
        CurrentStatusText = _isCurrent ? localizationService.GetString("RemoteCurrentStatus") : "";
    }

    public GitRemote Remote { get; }

    public bool IsCurrent => _isCurrent;

    public string Name => Remote.Name;

    public string ReferenceText { get; }

    public string CurrentStatusText { get; }

    public Visibility CurrentStatusVisibility => _isCurrent ? Visibility.Visible : Visibility.Collapsed;

    public bool CanSelect => !_isCurrent;

    public bool CanRename => !string.IsNullOrWhiteSpace(Name);

    public bool CanEdit => !string.IsNullOrWhiteSpace(ReferenceText);

    public bool CanRemove => !_isCurrent;

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void OnSelect()
    {
        _select(this);
    }

    [RelayCommand(CanExecute = nameof(CanRename), FlowExceptionsToTaskScheduler = true)]
    private Task OnRenameAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _rename(this));
    }

    [RelayCommand(CanExecute = nameof(CanEdit), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _edit(this));
    }

    [RelayCommand(CanExecute = nameof(CanRemove), FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _remove(this));
    }

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _copy(text);
        }
    }
}
