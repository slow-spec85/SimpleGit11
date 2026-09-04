using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.ViewModels;

public sealed partial class SubmoduleViewItem
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly Func<string, Task> _open;
    private readonly Action<string> _openFolder;
    private readonly Func<SubmoduleViewItem, SubmoduleAction, Task> _executeAction;
    private readonly Action<string> _copy;
    private readonly bool _canOpenLocalFolder;

    public SubmoduleViewItem(
        GitSubmodule submodule,
        ILocalizationService localizationService,
        IAsyncCommandExecutor asyncCommandExecutor,
        Func<string, Task> open,
        Action<string> openFolder,
        Func<SubmoduleViewItem, SubmoduleAction, Task> executeAction,
        Action<string> copy,
        string ownerRepositoryPath,
        bool canOpenLocalFolder = true)
    {
        Submodule = submodule ?? throw new ArgumentNullException(nameof(submodule));
        ArgumentNullException.ThrowIfNull(localizationService);
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _openFolder = openFolder ?? throw new ArgumentNullException(nameof(openFolder));
        _executeAction = executeAction ?? throw new ArgumentNullException(nameof(executeAction));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _canOpenLocalFolder = canOpenLocalFolder;
        OwnerRepositoryPath = ownerRepositoryPath;

        BranchText = string.IsNullOrWhiteSpace(submodule.Branch)
            ? localizationService.GetString("SubmoduleDefaultBranch")
            : submodule.Branch;
        CommitText = CreateCommitText(submodule);
        StatusText = CreateStatusText(submodule, localizationService);
        IsStatusAccent = HasNonPinnedState(submodule);
        AutomationName = string.Format(
            localizationService.GetString("SubmoduleAutomationName"),
            submodule.DisplayName,
            submodule.Path,
            StatusText);
        Children = new ObservableCollection<SubmoduleViewItem>();
        foreach (GitSubmodule child in submodule.Children)
        {
            Children.Add(new SubmoduleViewItem(
                child,
                localizationService,
                asyncCommandExecutor,
                open,
                openFolder,
                executeAction,
                copy,
                submodule.FullPath,
                canOpenLocalFolder));
        }
    }

    public GitSubmodule Submodule { get; }

    public ObservableCollection<SubmoduleViewItem> Children { get; }

    public string Name => Submodule.DisplayName;

    public string Path => Submodule.Path;

    public string FullPath => Submodule.FullPath;

    public string Url => Submodule.Url;

    public string Branch => Submodule.Branch;

    public string BranchText { get; }

    public string OwnerRepositoryPath { get; }

    public string CommitText { get; }

    public string StatusText { get; }

    public bool IsStatusAccent { get; }

    public bool IsStatusNeutral => !IsStatusAccent;

    public string AutomationName { get; }

    public bool CanOpen => Submodule.IsInitialized && !Submodule.HasError;

    public bool CanOpenFolder => _canOpenLocalFolder && Directory.Exists(Submodule.FullPath);

    [RelayCommand(CanExecute = nameof(CanOpen), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenAsync()
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _open(Submodule.FullPath));
    }

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OnOpenFolder()
    {
        _openFolder(Submodule.FullPath);
    }

    [RelayCommand(CanExecute = nameof(CanInitialize), FlowExceptionsToTaskScheduler = true)]
    private Task OnInitializeAsync() => ExecuteActionAsync(SubmoduleAction.Initialize);

    [RelayCommand(CanExecute = nameof(CanUseInitializedModule), FlowExceptionsToTaskScheduler = true)]
    private Task OnCheckoutRecordedAsync() => ExecuteActionAsync(SubmoduleAction.CheckoutRecorded);

    [RelayCommand(CanExecute = nameof(CanUseInitializedModule), FlowExceptionsToTaskScheduler = true)]
    private Task OnUpdateFromRemoteAsync() => ExecuteActionAsync(SubmoduleAction.UpdateFromRemote);

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnSyncAsync() => ExecuteActionAsync(SubmoduleAction.Sync);

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnEditUrlAsync() => ExecuteActionAsync(SubmoduleAction.EditUrl);

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnEditBranchAsync() => ExecuteActionAsync(SubmoduleAction.EditBranch);

    [RelayCommand(CanExecute = nameof(CanUseInitializedModule), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeinitializeAsync() => ExecuteActionAsync(SubmoduleAction.Deinitialize);

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveAsync() => ExecuteActionAsync(SubmoduleAction.Remove);

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _copy(text);
        }
    }

    private bool CanInitialize() => !Submodule.IsInitialized;

    private bool CanUseInitializedModule() => Submodule.IsInitialized;

    private Task ExecuteActionAsync(SubmoduleAction action)
    {
        return _asyncCommandExecutor.ExecuteAsync(() => _executeAction(this, action));
    }

    private static string CreateCommitText(GitSubmodule submodule)
    {
        if (!submodule.IsInitialized)
        {
            return submodule.ShortIndexCommit;
        }

        return submodule.IsCommitChanged
            ? $"{submodule.ShortIndexCommit} → {submodule.ShortCheckedOutCommit}"
            : submodule.ShortCheckedOutCommit;
    }

    private static string CreateStatusText(
        GitSubmodule submodule,
        ILocalizationService localizationService)
    {
        if (submodule.HasError)
        {
            return localizationService.GetString("SubmoduleStatusError");
        }

        if (!submodule.IsInitialized)
        {
            return localizationService.GetString("SubmoduleStatusNotInitialized");
        }

        List<string> statuses = [];
        if (submodule.HasConflict)
        {
            statuses.Add(localizationService.GetString("SubmoduleStatusConflict"));
        }
        if (submodule.IsCommitChanged)
        {
            statuses.Add(localizationService.GetString("SubmoduleStatusDifferentCommit"));
        }
        if (submodule.IsStaged)
        {
            statuses.Add(localizationService.GetString("SubmoduleStatusStaged"));
        }
        if (submodule.HasTrackedChanges)
        {
            statuses.Add(localizationService.GetString("SubmoduleStatusTrackedChanges"));
        }
        if (submodule.HasUntrackedFiles)
        {
            statuses.Add(localizationService.GetString("SubmoduleStatusUntrackedFiles"));
        }

        return statuses.Count == 0
            ? localizationService.GetString("SubmoduleStatusReady")
            : string.Join(" · ", statuses);
    }

    private static bool HasNonPinnedState(GitSubmodule submodule) =>
        submodule.HasError
        || !submodule.IsInitialized
        || submodule.HasConflict
        || submodule.IsCommitChanged
        || submodule.IsStaged
        || submodule.HasTrackedChanges
        || submodule.HasUntrackedFiles;
}
