using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed partial class WorktreeViewItem
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly Action<WorktreeViewItem> _openFolder;
    private readonly Func<WorktreeViewItem, Task> _open;
    private readonly Func<WorktreeViewItem, Task> _move;
    private readonly Func<WorktreeViewItem, Task> _remove;
    private readonly Func<WorktreeViewItem, Task> _toggleLock;
    private readonly Action<string> _copy;
    private readonly bool _canOpenLocalFolder;

    public WorktreeViewItem(
        GitWorktree worktree,
        ILocalizationService localizationService,
        IAsyncCommandExecutor asyncCommandExecutor,
        Action<WorktreeViewItem> openFolder,
        Func<WorktreeViewItem, Task> open,
        Func<WorktreeViewItem, Task> move,
        Func<WorktreeViewItem, Task> remove,
        Func<WorktreeViewItem, Task> toggleLock,
        Action<string> copy,
        bool canOpenLocalFolder = true)
    {
        Worktree = worktree;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        _openFolder = openFolder ?? throw new ArgumentNullException(nameof(openFolder));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _move = move ?? throw new ArgumentNullException(nameof(move));
        _remove = remove ?? throw new ArgumentNullException(nameof(remove));
        _toggleLock = toggleLock ?? throw new ArgumentNullException(nameof(toggleLock));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _canOpenLocalFolder = canOpenLocalFolder;
        ReferenceText = string.IsNullOrWhiteSpace(worktree.BranchName)
            ? string.Format(localizationService.GetString("WorktreeDetachedHead"), worktree.ShortHeadHash)
            : worktree.BranchName;
        TypeText = localizationService.GetString(
            worktree.IsMain ? "MainWorktreeType" : "LocalWorktreeType");
        CurrentStatusText = worktree.IsCurrent
            ? localizationService.GetString("WorktreeCurrentStatus")
            : "";
        List<string> statuses = [];
        if (worktree.IsLocked)
        {
            statuses.Add(localizationService.GetString("WorktreeLockedStatus"));
        }
        if (worktree.IsPrunable)
        {
            statuses.Add(localizationService.GetString("WorktreePrunableStatus"));
        }
        AuxiliaryStatusText = string.Join(" · ", statuses);
    }

    public GitWorktree Worktree { get; }

    public string Name => Worktree.DisplayName;

    public string Path => Worktree.Path;

    public string ReferenceText { get; }

    public string TypeText { get; }

    public string CurrentStatusText { get; }

    public string AuxiliaryStatusText { get; }

    public Visibility CurrentStatusVisibility => Worktree.IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AuxiliaryStatusVisibility => string.IsNullOrWhiteSpace(AuxiliaryStatusText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool CanOpen => !Worktree.IsCurrent && !Worktree.IsBare && !Worktree.IsPrunable;

    public bool CanOpenFolder => _canOpenLocalFolder && !Worktree.IsBare && !Worktree.IsPrunable;

    public bool CanMove => !Worktree.IsMain && !Worktree.IsLocked && !Worktree.IsPrunable;

    public bool CanRemove => !Worktree.IsMain && !Worktree.IsLocked && !Worktree.IsPrunable;

    public bool CanToggleLock => !Worktree.IsMain && !Worktree.IsPrunable;

    public Visibility LockVisibility => Worktree.IsLocked ? Visibility.Collapsed : Visibility.Visible;

    public Visibility UnlockVisibility => Worktree.IsLocked ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OnOpenFolder()
    {
        _openFolder(this);
    }

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _open(this));
    }

    [RelayCommand(CanExecute = nameof(CanMove), FlowExceptionsToTaskScheduler = true)]
    private Task OnMoveAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _move(this));
    }

    [RelayCommand(CanExecute = nameof(CanRemove), FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _remove(this));
    }

    [RelayCommand(CanExecute = nameof(CanToggleLock), FlowExceptionsToTaskScheduler = true)]
    private Task OnToggleLockAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _toggleLock(this));
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
