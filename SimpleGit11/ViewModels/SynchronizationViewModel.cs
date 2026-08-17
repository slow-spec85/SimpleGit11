using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;

namespace SimpleGit11.ViewModels;

public sealed partial class SynchronizationViewModel : AppNotificationViewModelBase
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IGitService _gitService;
    private readonly ILocalizationService _localizationService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _remoteOperationCancellationTokenSource;
    private bool _hasRefreshed;
    private bool _isUpdatingRemoteSelection;
    private string? _lastRepositoryPath;
    private string? _snapshotRepositoryPath;
    private DateTimeOffset? _lastSuccessfulFetch;

    public SynchronizationViewModel(
        MainWindowViewModel mainWindowViewModel,
        IGitService gitService,
        ILocalizationService localizationService,
        IClipboardService clipboardService,
        IDialogService dialogService,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _mainWindowViewModel = mainWindowViewModel;
        _gitService = gitService;
        _localizationService = localizationService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        Remotes = [];
        RemoteOptions = [];
        LocalBranches = [];
        Tags = [];
        IncomingBranches = [];
        ProgressMessage = "";
    }

    [ObservableProperty]
    public partial IReadOnlyList<GitRemote> Remotes { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<RemoteSelectionItem> RemoteOptions { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<BranchSynchronizationViewItem> LocalBranches { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<TagSynchronizationViewItem> Tags { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<BranchSynchronizationViewItem> IncomingBranches { get; private set; }

    [RelayCommand(CanExecute = nameof(CanRefreshSynchronization), FlowExceptionsToTaskScheduler = true)]
    private Task OnRefreshSynchronizationAsync() =>
        _asyncCommandExecutor.ExecuteAsync(RefreshSynchronizationAsync);

    private bool CanRefreshSynchronization() => !IsGitOperationRunning;

    [RelayCommand(CanExecute = nameof(CanPull), FlowExceptionsToTaskScheduler = true)]
    private Task OnPullAsync() => _asyncCommandExecutor.ExecuteAsync(PullAsync);

    [RelayCommand(CanExecute = nameof(CanPushAllChanges), FlowExceptionsToTaskScheduler = true)]
    private Task OnPushAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => PushAllChangesAsync(GitPushMode.Regular));

    [RelayCommand(CanExecute = nameof(CanPushAllChanges), FlowExceptionsToTaskScheduler = true)]
    private Task OnAtomicPushAsync() =>
        _asyncCommandExecutor.ExecuteAsync(() => PushAllChangesAsync(GitPushMode.Atomic));

    [RelayCommand(CanExecute = nameof(CanPushBranches), FlowExceptionsToTaskScheduler = true)]
    private Task OnPushAllBranchesAsync() => _asyncCommandExecutor.ExecuteAsync(PushAllBranchesAsync);

    [RelayCommand(CanExecute = nameof(CanPushTags), FlowExceptionsToTaskScheduler = true)]
    private Task OnPushAllTagsAsync() => _asyncCommandExecutor.ExecuteAsync(PushAllTagsAsync);

    [RelayCommand(CanExecute = nameof(CanPushBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnPushBranchAsync(BranchSynchronizationViewItem? branch) =>
        _asyncCommandExecutor.ExecuteAsync(() => PushBranchAsync(branch));

    private bool CanPushBranch(BranchSynchronizationViewItem? branch) =>
        CanRunRemoteOperation && branch is { CanPush: true };

    [RelayCommand(CanExecute = nameof(CanPushTag), FlowExceptionsToTaskScheduler = true)]
    private Task OnPushTagAsync(TagSynchronizationViewItem? tag) =>
        _asyncCommandExecutor.ExecuteAsync(() => PushTagAsync(tag));

    private bool CanPushTag(TagSynchronizationViewItem? tag) =>
        CanRunRemoteOperation && tag is { CanPush: true };

    [RelayCommand(CanExecute = nameof(CanCancelRemoteOperation))]
    private void OnCancelRemoteOperation() => CancelRemoteOperation();

    [RelayCommand(CanExecute = nameof(CanAddRemote), FlowExceptionsToTaskScheduler = true)]
    private Task OnAddRemoteAsync() => _asyncCommandExecutor.ExecuteAsync(AddRemoteAsync);

    [RelayCommand(CanExecute = nameof(CanRunRemoteOperation), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditRemoteUrlAsync() => _asyncCommandExecutor.ExecuteAsync(EditRemoteUrlAsync);

    [RelayCommand(CanExecute = nameof(CanRemoveRemote), FlowExceptionsToTaskScheduler = true)]
    private Task OnRemoveRemoteAsync() => _asyncCommandExecutor.ExecuteAsync(RemoveRemoteAsync);

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _clipboardService.SetText(text);
        }
    }

    [ObservableProperty]
    public partial GitRemote? SelectedRemote { get; set; }

    [ObservableProperty]
    public partial RemoteSelectionItem? SelectedRemoteOption { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSynchronized))]
    [NotifyPropertyChangedFor(nameof(HasSynchronizationContent))]
    [NotifyPropertyChangedFor(nameof(CanPull))]
    public partial SynchronizationSnapshot? Snapshot { get; private set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; private set; }

    [ObservableProperty]
    public partial bool IsGitOperationRunning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSynchronizationContent))]
    public partial bool HasNoRemotes { get; private set; }

    partial void OnRemotesChanged(IReadOnlyList<GitRemote> value)
    {
        UpdateRemoteOptions();
    }

    partial void OnSelectedRemoteChanged(GitRemote? value)
    {
        _mainWindowViewModel.SelectRemote(value?.Name);
        SyncSelectedRemoteOption();
        UpdateCommandStates();
        if (_hasRefreshed && !_isUpdatingRemoteSelection && !IsGitOperationRunning)
        {
            _ = RefreshSelectedRemoteAsync();
        }
    }

    partial void OnSelectedRemoteOptionChanged(RemoteSelectionItem? value)
    {
        if (_isUpdatingRemoteSelection)
        {
            return;
        }

        if (value?.IsAddNew == true)
        {
            _ = AddRemoteFromSelectorAsync();
            return;
        }

        SelectedRemote = value?.Remote;
    }

    partial void OnProgressMessageChanged(string value)
    {
        PublishSynchronizationOperationState();
    }

    partial void OnIsGitOperationRunningChanged(bool value)
    {
        UpdateCommandStates();
        PublishSynchronizationOperationState();
    }

    public bool IsSynchronized => Snapshot?.IsSynchronized == true;

    public bool HasSynchronizationContent => Snapshot is not null && !IsSynchronized && !HasNoRemotes;

    public bool HasLocalChanges => LocalBranches.Count > 0 || Tags.Count > 0;

    public bool HasBranchChanges => LocalBranches.Count > 0;

    public bool HasTagChanges => Tags.Count > 0;

    public int BranchChangesCount => LocalBranches.Count;

    public int TagChangesCount => Tags.Count;

    public bool HasRemoteChanges => IncomingBranches.Count > 0;

    public string LastSuccessfulFetchText => _lastSuccessfulFetch is null
        ? _localizationService.GetString("SynchronizationFetchNotPerformed")
        : string.Format(
            _localizationService.GetString("SynchronizationLastFetch"),
            _lastSuccessfulFetch.Value.ToString("g", CultureInfo.CurrentCulture));

    public bool CanRunRemoteOperation => SelectedRemote is not null
        && _mainWindowViewModel.CurrentRepository is not null
        && !IsGitOperationRunning;

    public bool CanAddRemote => _mainWindowViewModel.CurrentRepository is not null
        && !IsGitOperationRunning;

    public bool CanUseRemoteSelector => _mainWindowViewModel.CurrentRepository is not null
        && !IsGitOperationRunning;

    public bool CanRemoveRemote => CanRunRemoteOperation;

    public bool CanPull => CanRunRemoteOperation
        && Snapshot?.CurrentBranch?.HasIncomingCommits == true;

    public bool CanPushBranches => CanRunRemoteOperation
        && LocalBranches.Any(item => item.CanPush);

    public bool CanPushTags => CanRunRemoteOperation
        && Tags.Any(item => item.CanPush);

    public bool CanPushAllChanges => CanPushBranches || CanPushTags;

    public bool IsRemoteOperationCancelable => _remoteOperationCancellationTokenSource is not null;

    public bool CanCancelRemoteOperation => _remoteOperationCancellationTokenSource is
    {
        IsCancellationRequested: false
    };

    public void InitializeDispatcherQueue(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _dispatcherQueue = dispatcherQueue;
    }

    public Task RefreshSynchronizationLocalAsync()
    {
        string? currentRepositoryPath = _mainWindowViewModel.CurrentRepository?.Path;
        bool hasKnownRemoteState = Snapshot is not null
            && _snapshotRepositoryPath == currentRepositoryPath
            && Snapshot.Remote.Name == SelectedRemote?.Name;
        return RunGitOperationAsync(
            _localizationService.GetString("RefreshingSynchronizationProgress"),
            async cancellationToken =>
            {
                await RefreshCoreAsync(
                    fetch: false,
                    useKnownRemoteTagState: hasKnownRemoteState,
                    cancellationToken);
            },
            canCancel: !hasKnownRemoteState);
    }

    private async Task RefreshSynchronizationAsync()
    {
        await RunGitOperationAsync(
            _localizationService.GetString("RefreshingSynchronizationProgress"),
            cancellationToken => RefreshCoreAsync(
                fetch: true,
                useKnownRemoteTagState: false,
                cancellationToken),
            canCancel: true);
    }

    private Task RefreshSelectedRemoteAsync()
    {
        return RunGitOperationAsync(
            _localizationService.GetString("RefreshingSynchronizationProgress"),
            cancellationToken => RefreshSnapshotAsync(cancellationToken),
            canCancel: true);
    }

    private async Task RefreshCoreAsync(
        bool fetch,
        bool useKnownRemoteTagState,
        CancellationToken cancellationToken)
    {
        ClearNotification();
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            ClearSynchronizationState();
            ShowError(_localizationService.GetString("OpenRepositoryBeforeSynchronization"));
            return;
        }

        try
        {
            string? selectedRemoteName = string.IsNullOrWhiteSpace(_mainWindowViewModel.SelectedRemoteName)
                ? SelectedRemote?.Name
                : _mainWindowViewModel.SelectedRemoteName;
            IReadOnlyList<GitRemote> remotes = await _gitService.GetRemotesAsync(repository, cancellationToken);
            _lastRepositoryPath = repository.Path;
            _hasRefreshed = true;

            _isUpdatingRemoteSelection = true;
            Remotes = remotes.ToList();

            SelectedRemote = Remotes.FirstOrDefault(remote => remote.Name == selectedRemoteName)
                ?? Remotes.FirstOrDefault(remote => remote.Name == "origin")
                ?? Remotes.FirstOrDefault();
            _isUpdatingRemoteSelection = false;
            HasNoRemotes = SelectedRemote is null;

            _lastSuccessfulFetch = await _gitService.GetLastFetchTimeAsync(repository, cancellationToken);
            OnPropertyChanged(nameof(LastSuccessfulFetchText));

            if (SelectedRemote is null)
            {
                ClearSnapshot();
                return;
            }

            if (fetch)
            {
                await _gitService.Remotes.FetchSynchronizationRemotesAsync(
                    repository,
                    SelectedRemote,
                    cancellationToken);
                _lastSuccessfulFetch = await _gitService.GetLastFetchTimeAsync(repository, cancellationToken);
                OnPropertyChanged(nameof(LastSuccessfulFetchText));
            }

            await RefreshSnapshotAsync(cancellationToken, useKnownRemoteTagState);
        }
        catch (Exception exception) when (IsExpectedGitException(exception))
        {
            ShowGitError(exception);
        }
        finally
        {
            _isUpdatingRemoteSelection = false;
        }
    }

    private async Task RefreshSnapshotAsync(
        CancellationToken cancellationToken,
        bool useKnownRemoteTagState = false)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote? remote = SelectedRemote;
        if (repository is null || remote is null)
        {
            ClearSnapshot();
            return;
        }

        try
        {
            SynchronizationSnapshot snapshot;
            if (useKnownRemoteTagState
                && Snapshot is SynchronizationSnapshot knownSnapshot
                && _snapshotRepositoryPath == repository.Path
                && knownSnapshot.Remote.Name == remote.Name)
            {
                snapshot = await _gitService.Remotes.GetLocalConfiguredSynchronizationSnapshotAsync(
                    repository,
                    remote,
                    knownSnapshot.Tags,
                    cancellationToken);
            }
            else
            {
                snapshot = await _gitService.Remotes.GetConfiguredSynchronizationSnapshotAsync(
                    repository,
                    remote,
                    cancellationToken);
            }
            if (_mainWindowViewModel.CurrentRepository?.Path != repository.Path
                || SelectedRemote?.Name != remote.Name)
            {
                return;
            }

            ApplySynchronizationSnapshot(snapshot, repository.Path);
        }
        catch (Exception exception) when (IsExpectedGitException(exception))
        {
            ShowGitError(exception);
        }
    }

    private void ApplySynchronizationSnapshot(SynchronizationSnapshot snapshot, string repositoryPath)
    {
        IReadOnlyList<BranchSynchronizationViewItem> localBranches = snapshot.OutgoingBranches
            .Select(branch => new BranchSynchronizationViewItem(
                branch,
                _localizationService,
                BranchSynchronizationDirection.Outgoing))
            .ToList();
        IReadOnlyList<TagSynchronizationViewItem> tags = snapshot.Tags
            .Where(item => item.NeedsSynchronization)
            .Select(tag => new TagSynchronizationViewItem(tag, snapshot.Remote, _localizationService))
            .ToList();
        IReadOnlyList<BranchSynchronizationViewItem> incomingBranches = snapshot.IncomingBranches
            .Select(branch => new BranchSynchronizationViewItem(
                branch,
                _localizationService,
                BranchSynchronizationDirection.Incoming))
            .ToList();

        Snapshot = snapshot;
        _snapshotRepositoryPath = repositoryPath;
        LocalBranches = localBranches;
        Tags = tags;
        IncomingBranches = incomingBranches;

        NotifySynchronizationStateChanged();
    }

    private Task PullAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        BranchSynchronizationItem? currentBranch = Snapshot?.CurrentBranch;
        GitRemote? defaultRemote = SelectedRemote;
        if (repository is null || currentBranch is null || defaultRemote is null)
        {
            return Task.CompletedTask;
        }

        return RunRemoteOperationAsync(
            _localizationService.GetString("PullingProgress"),
            async cancellationToken =>
            {
                GitRemoteOperationResult result = currentBranch.HasUpstream
                    ? await _gitService.Remotes.PullAsync(repository, cancellationToken)
                    : await _gitService.Remotes.PullAsync(
                        repository,
                        defaultRemote.Name,
                        currentBranch.Name,
                        cancellationToken);
                _lastSuccessfulFetch = await _gitService.GetLastFetchTimeAsync(repository, cancellationToken);
                OnPropertyChanged(nameof(LastSuccessfulFetchText));
                return result;
            },
            _localizationService.GetString("PullSucceeded"));
    }

    private Task PushAllChangesAsync(GitPushMode mode)
    {
        return ExecutePushBatchAsync(
            LocalBranches.Where(item => item.CanPush).Select(item => item.Branch),
            Tags.Where(item => item.CanPush).Select(item => item.Tag),
            mode,
            mode == GitPushMode.Atomic
                ? "SynchronizationPushingAllAtomicallyProgress"
                : "SynchronizationPushingAllProgress",
            mode == GitPushMode.Atomic
                ? "SynchronizationAtomicPushAllSucceeded"
                : "SynchronizationPushAllSucceeded");
    }

    private Task PushAllBranchesAsync()
    {
        return ExecutePushBatchAsync(
            LocalBranches.Where(item => item.CanPush).Select(item => item.Branch),
            [],
            GitPushMode.Regular,
            "SynchronizationPushingBranchesProgress",
            "SynchronizationPushBranchesSucceeded");
    }

    private Task PushAllTagsAsync()
    {
        return ExecutePushBatchAsync(
            [],
            Tags.Where(item => item.CanPush).Select(item => item.Tag),
            GitPushMode.Regular,
            "SynchronizationPushingTagsProgress",
            "SynchronizationPushTagsSucceeded");
    }

    private Task PushBranchAsync(object? parameter)
    {
        if (parameter is not BranchSynchronizationViewItem item || !item.CanPush)
        {
            return Task.CompletedTask;
        }

        return ExecutePushBatchAsync(
            [item.Branch],
            [],
            GitPushMode.Regular,
            "SynchronizationPushingBranchProgress",
            "SynchronizationPushBranchSucceeded",
            item.Name);
    }

    private Task PushTagAsync(object? parameter)
    {
        if (parameter is not TagSynchronizationViewItem item || !item.CanPush)
        {
            return Task.CompletedTask;
        }

        return ExecutePushBatchAsync(
            [],
            [item.Tag],
            GitPushMode.Regular,
            "SynchronizationPushingTagProgress",
            "SynchronizationPushTagSucceeded",
            item.Name);
    }

    private async Task ExecutePushBatchAsync(
        IEnumerable<BranchSynchronizationItem> branches,
        IEnumerable<TagSynchronizationItem> tags,
        GitPushMode mode,
        string progressKey,
        string successKey,
        string? itemName = null)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote? remote = SelectedRemote;
        if (repository is null || remote is null)
        {
            return;
        }

        IReadOnlyList<BranchSynchronizationItem> branchList = branches.ToList();
        IReadOnlyList<TagSynchronizationItem> tagList = tags.ToList();
        List<BranchPushOperation> branchOperations = [];
        foreach (BranchSynchronizationItem branch in branchList)
        {
            string targetRemoteName = branch.ConfiguredPushRemoteName;
            if (string.IsNullOrWhiteSpace(targetRemoteName))
            {
                ShowError(string.Format(
                    _localizationService.GetString("BranchPushRemoteUnavailable"),
                    branch.Name));
                return;
            }

            bool untrackedBranchRequiresForce = branch.NeedsUpstream
                && branch.HasIncomingFromPushRemote;
            branchOperations.Add(new BranchPushOperation(
                branch,
                targetRemoteName,
                branch.RequiresForcePush || untrackedBranchRequiresForce));
        }

        IReadOnlyList<BranchPushOperation> operationsUsingConfiguredRemote = branchOperations
            .Where(operation => !string.Equals(
                operation.RemoteName,
                remote.Name,
                StringComparison.Ordinal))
            .ToList();
        if (operationsUsingConfiguredRemote.Count > 0)
        {
            string destinations = string.Join(
                Environment.NewLine,
                operationsUsingConfiguredRemote.Select(operation => $"{operation.Branch.Name} → {operation.RemoteName}"));
            bool confirmed = await _dialogService.ConfirmAsync(
                _localizationService.GetString("PushUsesConfiguredRemoteDialogTitle"),
                string.Format(
                    _localizationService.GetString("PushUsesConfiguredRemoteDialogMessage"),
                    remote.Name,
                    destinations),
                _localizationService.GetString("PushUsesConfiguredRemoteDialogPrimaryButton"));
            if (!confirmed)
            {
                return;
            }
        }

        IReadOnlyList<BranchPushOperation> branchesRequiringForce = branchOperations
            .Where(operation => operation.ForceWithLease)
            .ToList();
        if (branchesRequiringForce.Count > 0)
        {
            string branchNames = string.Join(", ", branchesRequiringForce.Select(operation => operation.Branch.Name));
            string remoteNames = string.Join(", ", branchesRequiringForce
                .Select(operation => operation.RemoteName)
                .Distinct(StringComparer.Ordinal));
            bool confirmed = await _dialogService.ConfirmAsync(
                _localizationService.GetString("ForcePushDialogTitle"),
                string.Format(
                    _localizationService.GetString("ForcePushDialogMessage"),
                    branchNames,
                    remoteNames),
                _localizationService.GetString("ForcePushDialogPrimaryButton"));
            if (!confirmed)
            {
                return;
            }
        }

        string progress = itemName is null
            ? _localizationService.GetString(progressKey)
            : string.Format(_localizationService.GetString(progressKey), itemName);
        List<(string RemoteName, GitPushReferenceUpdate Reference)> referenceDestinations =
            branchOperations
                .Select(operation => (
                    operation.RemoteName,
                    new GitPushReferenceUpdate(
                        GitPushReferenceKind.Branch,
                        operation.Branch.Name,
                        operation.ForceWithLease)))
                .ToList();
        referenceDestinations.AddRange(tagList.Select(tag => (
            remote.Name,
            new GitPushReferenceUpdate(GitPushReferenceKind.Tag, tag.Name))));

        IReadOnlyList<GitPushRequest> pushRequests = referenceDestinations
            .GroupBy(destination => destination.RemoteName, StringComparer.Ordinal)
            .Select(group => new GitPushRequest(
                group.Key,
                group.Select(destination => destination.Reference).ToList(),
                mode))
            .ToList();
        IReadOnlyList<string> destinationsUsed = pushRequests
            .Select(request => request.RemoteName)
            .ToList();
        string destinationText = string.Join(", ", destinationsUsed);
        string success = destinationsUsed.Count <= 1
            ? itemName is null
                ? string.Format(_localizationService.GetString(successKey), destinationText)
                : string.Format(_localizationService.GetString(successKey), itemName, destinationText)
            : string.Format(
                _localizationService.GetString(mode == GitPushMode.Atomic
                    ? "SynchronizationAtomicPushMultipleRemotesSucceeded"
                    : "SynchronizationPushMultipleRemotesSucceeded"),
                destinationText);

        await RunRemoteOperationAsync(progress, async cancellationToken =>
        {
            List<string> outputs = [];
            foreach (GitPushRequest request in pushRequests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GitRemoteOperationResult result = await _gitService.Remotes.PushAsync(
                    repository,
                    request,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    outputs.Add(result.Output.Trim());
                }
            }

            return new GitRemoteOperationResult(
                string.Join(Environment.NewLine + Environment.NewLine, outputs));
        }, success);
    }

    private sealed record BranchPushOperation(
        BranchSynchronizationItem Branch,
        string RemoteName,
        bool ForceWithLease);

    private async Task AddRemoteAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeSynchronization"));
            return;
        }

        string? remoteName = await GetRemoteNameForNewRemoteAsync(Remotes);
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return;
        }

        string? remoteUrl = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            string.Format(_localizationService.GetString("AddRemoteUrlDialogTitle"), remoteName),
            _localizationService.GetString("AddRemoteUrlDialogTextBoxHeader"),
            "",
            _localizationService.GetString("AddRemoteUrlDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("RemoteUrlPlaceholder")));

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return;
        }

        await RunRemoteOperationAsync(
            string.Format(_localizationService.GetString("AddRemoteProgress"), remoteName),
            cancellationToken => _gitService.Remotes.AddRemoteAsync(repository, remoteName, remoteUrl, cancellationToken),
            string.Format(_localizationService.GetString("RemoteAdded"), remoteName));

        await RefreshSynchronizationLocalAsync();

        GitRemote? addedRemote = Remotes.FirstOrDefault(remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal));
        if (addedRemote is not null)
        {
            SelectedRemote = addedRemote;
        }
    }

    private async Task AddRemoteFromSelectorAsync()
    {
        GitRemote? previousRemote = SelectedRemote;
        await AddRemoteAsync();
        if (SelectedRemote is null && previousRemote is not null)
        {
            SelectedRemote = previousRemote;
        }

        SyncSelectedRemoteOption();
    }

    private async Task<string?> GetRemoteNameForNewRemoteAsync(IReadOnlyList<GitRemote> remotes)
    {
        if (remotes.Count == 0)
        {
            return "origin";
        }

        string defaultRemoteName = GetDefaultRemoteName(remotes);
        string? remoteName = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            _localizationService.GetString("AddRemoteNameDialogTitle"),
            _localizationService.GetString("AddRemoteNameDialogTextBoxHeader"),
            defaultRemoteName,
            _localizationService.GetString("AddRemoteNameDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("AddRemoteNameDialogPlaceholder")));

        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return null;
        }

        if (!IsValidRemoteName(remoteName))
        {
            ShowError(_localizationService.GetString("RemoteNameInvalid"));
            return null;
        }

        if (remotes.Any(remote => string.Equals(remote.Name, remoteName, StringComparison.Ordinal)))
        {
            ShowError(string.Format(_localizationService.GetString("RemoteAlreadyExists"), remoteName));
            return null;
        }

        return remoteName;
    }

    private async Task EditRemoteUrlAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote? remote = SelectedRemote;
        if (repository is null || remote is null)
        {
            return;
        }

        string currentUrl = remote.DisplayUrl;
        string? newUrl = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            string.Format(_localizationService.GetString("EditRemoteUrlDialogTitle"), remote.Name),
            _localizationService.GetString("EditRemoteUrlDialogTextBoxHeader"),
            currentUrl,
            _localizationService.GetString("EditRemoteUrlDialogPrimaryButton"),
            _localizationService.GetString("TextInputDialogCancelButton"),
            _localizationService.GetString("RemoteUrlPlaceholder")));

        if (string.IsNullOrWhiteSpace(newUrl) || newUrl == currentUrl)
        {
            return;
        }

        await RunRemoteOperationAsync(
            string.Format(_localizationService.GetString("EditRemoteUrlProgress"), remote.Name),
            _ => _gitService.Remotes.SetRemoteUrlAsync(repository, remote, newUrl),
            string.Format(_localizationService.GetString("RemoteUrlUpdated"), remote.Name));

        await RefreshSynchronizationLocalAsync();
    }

    private async Task RemoveRemoteAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitRemote? remote = SelectedRemote;
        if (repository is null || remote is null)
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            string.Format(_localizationService.GetString("RemoveRemoteDialogTitle"), remote.Name),
            string.Format(_localizationService.GetString("RemoveRemoteDialogMessage"), remote.Name),
            _localizationService.GetString("RemoveRemoteDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await RunGitOperationAsync(
            string.Format(_localizationService.GetString("RemoveRemoteProgress"), remote.Name),
            async cancellationToken =>
            {
                try
                {
                    ClearNotification();
                    GitRemoteOperationResult result = await _gitService.Remotes.RemoveRemoteAsync(repository, remote, cancellationToken);
                    await RefreshCoreAsync(fetch: false, useKnownRemoteTagState: false, cancellationToken);
                    string successMessage = string.Format(_localizationService.GetString("RemoteRemoved"), remote.Name);
                    ShowNotification(AppNotificationSeverity.Success, successMessage, result.Output);
                }
                catch (Exception exception) when (IsExpectedGitException(exception))
                {
                    await RefreshCoreAsync(fetch: false, useKnownRemoteTagState: false, cancellationToken);
                    ShowGitError(exception);
                }
            },
            canCancel: true);
    }

    private Task RunRemoteOperationAsync(
        string progressMessage,
        Func<CancellationToken, Task<GitRemoteOperationResult>> operation,
        string successMessage)
    {
        return RunGitOperationAsync(progressMessage, async cancellationToken =>
        {
            try
            {
                ClearNotification();
                GitRemoteOperationResult result = await operation(cancellationToken);
                await RefreshSnapshotAsync(cancellationToken);
                ShowNotification(AppNotificationSeverity.Success, successMessage, result.Output);
            }
            catch (Exception exception) when (IsExpectedGitException(exception))
            {
                await RefreshSnapshotAsync(cancellationToken);
                ShowGitError(exception);
                if (exception is GitRemoteOperationException { Kind: GitRemoteOperationErrorKind.Conflict })
                {
                    _mainWindowViewModel.RequestChangesNavigation(
                        _localizationService.GetString("ConflictResolutionRequiredOnChangesPage"),
                        exception.Message);
                }
            }
        }, canCancel: true);
    }

    private async Task RunGitOperationAsync(
        string progressMessage,
        Func<CancellationToken, Task> operation,
        bool canCancel)
    {
        await _gitService.ExecuteAsync(() => RunOnUiThreadAsync(async () =>
        {
            using CancellationTokenSource? cancellationTokenSource = canCancel
                ? new CancellationTokenSource()
                : null;
            _remoteOperationCancellationTokenSource = cancellationTokenSource;
            NotifyCancellationStateChanged();
            IsGitOperationRunning = true;
            ProgressMessage = progressMessage;
            try
            {
                await operation(cancellationTokenSource?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationTokenSource?.IsCancellationRequested == true)
            {
                ShowNotification(
                    AppNotificationSeverity.Informational,
                    _localizationService.GetString("SynchronizationOperationCanceled"));
            }
            catch (COMException exception)
            {
                Debug.WriteLine(exception);
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"));
            }
            finally
            {
                _remoteOperationCancellationTokenSource = null;
                NotifyCancellationStateChanged();
                ProgressMessage = "";
                IsGitOperationRunning = false;
            }
        }));
    }

    private void CancelRemoteOperation()
    {
        _remoteOperationCancellationTokenSource?.Cancel();
        NotifyCancellationStateChanged();
    }

    private void NotifyCancellationStateChanged()
    {
        OnPropertyChanged(nameof(IsRemoteOperationCancelable));
        OnPropertyChanged(nameof(CanCancelRemoteOperation));
        CancelRemoteOperationCommand.NotifyCanExecuteChanged();
        PublishSynchronizationOperationState();
    }

    private void PublishSynchronizationOperationState()
    {
        PublishOperationState(
            IsGitOperationRunning,
            ProgressMessage,
            CanCancelRemoteOperation ? CancelRemoteOperationCommand : null);
    }

    private void ClearSynchronizationState()
    {
        Remotes = [];
        SelectedRemote = null;
        HasNoRemotes = false;
        ClearSnapshot();
    }

    private void ClearSnapshot()
    {
        Snapshot = null;
        _snapshotRepositoryPath = null;
        LocalBranches = [];
        Tags = [];
        IncomingBranches = [];
        NotifySynchronizationStateChanged();
    }

    private Task RunOnUiThreadAsync(Func<Task> operation)
    {
        DispatcherQueue dispatcherQueue = _dispatcherQueue
            ?? throw new InvalidOperationException("SynchronizationViewModel has not been attached to a DispatcherQueue.");
        if (dispatcherQueue.HasThreadAccess)
        {
            return operation();
        }

        TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await operation();
                completionSource.SetResult();
            }
            catch (Exception exception)
            {
                completionSource.SetException(exception);
            }
        });

        if (!enqueued)
        {
            completionSource.SetException(new InvalidOperationException("The synchronization update could not be queued on the UI thread."));
        }

        return completionSource.Task;
    }

    private void NotifySynchronizationStateChanged()
    {
        OnPropertyChanged(nameof(IsSynchronized));
        OnPropertyChanged(nameof(HasSynchronizationContent));
        OnPropertyChanged(nameof(HasLocalChanges));
        OnPropertyChanged(nameof(HasBranchChanges));
        OnPropertyChanged(nameof(HasTagChanges));
        OnPropertyChanged(nameof(BranchChangesCount));
        OnPropertyChanged(nameof(TagChangesCount));
        OnPropertyChanged(nameof(HasRemoteChanges));
        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        RefreshSynchronizationCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        AtomicPushCommand.NotifyCanExecuteChanged();
        PushAllBranchesCommand.NotifyCanExecuteChanged();
        PushAllTagsCommand.NotifyCanExecuteChanged();
        PushBranchCommand.NotifyCanExecuteChanged();
        PushTagCommand.NotifyCanExecuteChanged();
        CancelRemoteOperationCommand.NotifyCanExecuteChanged();
        AddRemoteCommand.NotifyCanExecuteChanged();
        EditRemoteUrlCommand.NotifyCanExecuteChanged();
        RemoveRemoteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRunRemoteOperation));
        OnPropertyChanged(nameof(CanAddRemote));
        OnPropertyChanged(nameof(CanUseRemoteSelector));
        OnPropertyChanged(nameof(CanRemoveRemote));
        OnPropertyChanged(nameof(CanPull));
        OnPropertyChanged(nameof(CanPushBranches));
        OnPropertyChanged(nameof(CanPushTags));
        OnPropertyChanged(nameof(CanPushAllChanges));
    }

    private static bool IsExpectedGitException(Exception exception)
    {
        return exception is FileNotFoundException
            or DirectoryNotFoundException
            or GitRemoteOperationException
            or GitCommandException;
    }

    private void ShowGitError(Exception exception)
    {
        string message = exception switch
        {
            FileNotFoundException => _localizationService.GetString("GitExecutableNotFound"),
            DirectoryNotFoundException => _localizationService.GetString("RepositoryFolderNotFound"),
            GitRemoteOperationException remoteException => GetRemoteOperationErrorMessage(remoteException),
            _ => _localizationService.GetString("GitRemoteCommandFailed")
        };
        string? details = exception is GitCommandException ? exception.Message : null;
        ShowError(message, details);
    }

    private string GetRemoteOperationErrorMessage(GitRemoteOperationException exception)
    {
        return exception.Kind switch
        {
            GitRemoteOperationErrorKind.Authentication => _localizationService.GetString("RemoteAuthenticationFailed"),
            GitRemoteOperationErrorKind.Conflict => _localizationService.GetString("RemoteOperationConflicts"),
            GitRemoteOperationErrorKind.NonFastForward => _localizationService.GetString("RemoteOperationNonFastForward"),
            GitRemoteOperationErrorKind.AtomicNotSupported =>
                _localizationService.GetString("RemoteAtomicPushNotSupported"),
            _ => _localizationService.GetString("GitRemoteCommandFailed")
        };
    }

    private void ShowError(string message)
    {
        ShowNotification(AppNotificationSeverity.Error, message);
    }

    private void ShowError(string message, string? details)
    {
        ShowNotification(AppNotificationSeverity.Error, message, details);
    }

    private void UpdateRemoteOptions()
    {
        List<RemoteSelectionItem> options =
        [
            RemoteSelectionItem.CreateAddNew(_localizationService.GetString("AddRemoteSelectionItemText"))
        ];
        options.AddRange(Remotes.Select(RemoteSelectionItem.CreateRemote));
        RemoteOptions = options;
        SyncSelectedRemoteOption();
    }

    private void SyncSelectedRemoteOption()
    {
        RemoteSelectionItem? selectedOption = SelectedRemote is null
            ? null
            : RemoteOptions.FirstOrDefault(option => option.Remote?.Name == SelectedRemote.Name);

        _isUpdatingRemoteSelection = true;
        SelectedRemoteOption = selectedOption;
        _isUpdatingRemoteSelection = false;
    }

    private static string GetDefaultRemoteName(IReadOnlyList<GitRemote> remotes)
    {
        if (remotes.All(remote => !string.Equals(remote.Name, "origin", StringComparison.Ordinal)))
        {
            return "origin";
        }

        if (remotes.All(remote => !string.Equals(remote.Name, "upstream", StringComparison.Ordinal)))
        {
            return "upstream";
        }

        int index = remotes.Count + 1;
        string name;
        do
        {
            name = $"remote-{index++}";
        }
        while (remotes.Any(remote => string.Equals(remote.Name, name, StringComparison.Ordinal)));

        return name;
    }

    private static bool IsValidRemoteName(string remoteName)
    {
        return !string.IsNullOrWhiteSpace(remoteName)
            && !remoteName.StartsWith("-", StringComparison.Ordinal)
            && !remoteName.Any(char.IsWhiteSpace)
            && !remoteName.Contains("..", StringComparison.Ordinal)
            && !remoteName.Contains("@{", StringComparison.Ordinal)
            && remoteName.IndexOfAny(['~', '^', ':', '?', '*', '[', '\\']) < 0;
    }
}

public sealed class RemoteSelectionItem
{
    private RemoteSelectionItem(GitRemote? remote, string name, bool isAddNew)
    {
        Remote = remote;
        Name = name;
        IsAddNew = isAddNew;
    }

    public GitRemote? Remote { get; }

    public string Name { get; }

    public bool IsAddNew { get; }

    public static RemoteSelectionItem CreateAddNew(string text)
    {
        return new RemoteSelectionItem(null, text, true);
    }

    public static RemoteSelectionItem CreateRemote(GitRemote remote)
    {
        return new RemoteSelectionItem(remote, remote.Name, false);
    }
}
