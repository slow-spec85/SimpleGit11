using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensions;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public enum ReferenceListKind
{
    Branches,
    Tags
}

public enum BranchListScope
{
    Local,
    Remote,
    All
}

public sealed partial class BranchesViewModel : AppNotificationViewModelBase
{
    private readonly IAsyncCommandExecutor _asyncCommandExecutor;
    private const int BranchHistoryPreviewCount = 5;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly RepositoryViewModel _repositoryViewModel;
    private readonly ILocalizationService _localizationService;
    private readonly IClipboardService _clipboardService;
    private readonly IDialogService _dialogService;
    private readonly IGitService _gitService;
    private readonly Dictionary<string, IReadOnlyList<GitTag>> _remoteTagCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _remoteTagLoadAttempts = new(StringComparer.Ordinal);
    private CancellationTokenSource? _remoteOperationCancellationTokenSource;
    private bool _isSynchronizingBranchRemoteSettings;
    private string _remoteBranchSynchronizationRemoteName = "";
    private DateTimeOffset? _lastRepositoryFetch;
    private bool _isLastRepositoryFetchLoaded;
    private IReadOnlyList<GitReflogEntry> _selectedBranchReflogEntries = [];
    private int _queuedReadOperationCount;
    private bool _areLocalTagsLoaded;
    private bool _isSelectedTagRelationLoaded;
    private bool _isSelectedTagSignatureLoaded;
    private bool _isSelectedBranchHistoryLoaded;
    private bool _showAllBranchHistory;
    private bool _hasRefreshed;
    private string? _lastRepositoryPath;
    private string? _tagHeadCommitHash;
    private GitBranch? _selectedBranch;
    private GitBranch? _selectedLocalBranch;
    private GitBranch? _selectedRemoteBranch;
    private GitTag? _selectedTag;

    private enum BranchDeleteResult
    {
        Deleted,
        NeedsForce,
        Failed
    }

    private enum BranchMergeResult
    {
        Canceled,
        Completed,
        CompletedWithoutCommit
    }

    public BranchesViewModel(
        MainWindowViewModel mainWindowViewModel,
        RepositoryViewModel repositoryViewModel,
        IGitService gitService,
        ILocalizationService localizationService,
        IClipboardService clipboardService,
        IDialogService dialogService,
        IMessenger messenger,
        IAsyncCommandExecutor asyncCommandExecutor)
        : base(messenger)
    {
        _mainWindowViewModel = mainWindowViewModel;
        _repositoryViewModel = repositoryViewModel;
        _gitService = gitService;
        _localizationService = localizationService;
        _clipboardService = clipboardService;
        _dialogService = dialogService;
        _asyncCommandExecutor = asyncCommandExecutor
            ?? throw new ArgumentNullException(nameof(asyncCommandExecutor));
        Remotes = [];
        BranchUpstreamRemoteOptions = [];
        BranchPushRemoteOptions = [];
        NewBranchName = "";
        SearchText = "";
        ProgressMessage = "";
        ReferenceKind = ReferenceListKind.Branches;
        BranchScope = BranchListScope.Local;
    }

    private bool CanRunWhenIdle() => !IsGitOperationRunning;
    private bool CanCreateBranchWorktree() => !IsGitOperationRunning && CanCreateSelectedBranchWorktree;
    private bool CanOpenBranchWorktree() => !IsGitOperationRunning && CanOpenSelectedBranchWorktree;
    private bool CanCreateTagWorktree() => !IsGitOperationRunning && SelectedTag is not null;
    private bool CanOpenTagWorktree() => !IsGitOperationRunning && HasSelectedTagWorktrees;
    [RelayCommand]
    private void OnSelectBranches() => SelectBranches();

    [RelayCommand(FlowExceptionsToTaskScheduler = true)]
    private Task OnSelectTagsAsync() => _asyncCommandExecutor.ExecuteAsync(SelectTagsAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnRefreshBranchesAsync() => _asyncCommandExecutor.ExecuteAsync(RefreshBranchesAsync);

    [RelayCommand(CanExecute = nameof(CanCreateReference), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateReferenceAsync() => _asyncCommandExecutor.ExecuteAsync(CreateSelectedReferenceKindAsync);

    [RelayCommand(CanExecute = nameof(CanFetchSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnFetchBranchAsync() => _asyncCommandExecutor.ExecuteAsync(FetchSelectedRemoteBranchAsync);

    [RelayCommand(CanExecute = nameof(CanFetchSelectedTag), FlowExceptionsToTaskScheduler = true)]
    private Task OnFetchTagAsync() => _asyncCommandExecutor.ExecuteAsync(FetchSelectedRemoteTagAsync);

    [RelayCommand(CanExecute = nameof(CanSetSelectedBranchUpstream), FlowExceptionsToTaskScheduler = true)]
    private Task OnSetBranchUpstreamAsync() =>
        _asyncCommandExecutor.ExecuteAsync(SetSelectedBranchUpstreamAsync);

    [RelayCommand(CanExecute = nameof(CanSetSelectedBranchPushRemote), FlowExceptionsToTaskScheduler = true)]
    private Task OnSetBranchPushRemoteAsync() =>
        _asyncCommandExecutor.ExecuteAsync(SetSelectedBranchPushRemoteAsync);

    [RelayCommand(CanExecute = nameof(CanCancelRemoteOperation))]
    private void OnCancelRemoteOperation() => CancelRemoteOperation();

    [RelayCommand(CanExecute = nameof(CanCreateBranchWorktree), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateBranchWorktreeAsync() =>
        _asyncCommandExecutor.ExecuteAsync(CreateSelectedBranchWorktreeAsync);

    [RelayCommand(CanExecute = nameof(CanOpenBranchWorktree), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenBranchWorktreeAsync() =>
        _asyncCommandExecutor.ExecuteAsync(OpenSelectedBranchWorktreeAsync);

    [RelayCommand(CanExecute = nameof(CanCreateTagWorktree), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateTagWorktreeAsync() =>
        _asyncCommandExecutor.ExecuteAsync(CreateSelectedTagWorktreeAsync);

    [RelayCommand(CanExecute = nameof(CanCreateTagWorktree), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateTagBranchWorktreeAsync() =>
        _asyncCommandExecutor.ExecuteAsync(CreateSelectedTagBranchWorktreeAsync);

    [RelayCommand(CanExecute = nameof(CanOpenTagWorktree), FlowExceptionsToTaskScheduler = true)]
    private Task OnOpenTagWorktreeAsync() =>
        _asyncCommandExecutor.ExecuteAsync(OpenSelectedTagWorktreeAsync);

    [RelayCommand]
    private void OnToggleBranchHistory() => ToggleBranchHistory();

    [RelayCommand(CanExecute = nameof(CanCheckoutSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnCheckoutBranchAsync() => _asyncCommandExecutor.ExecuteAsync(CheckoutBranchCoreAsync);

    private async Task CheckoutBranchCoreAsync()
    {
        if (SelectedBranch?.IsRemote ?? false)
        {
            await CheckoutRemoteBranchAsync();
        }
        else
        {
            await CheckoutSelectedBranchAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckoutSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnBranchPrimaryAsync() =>
        _asyncCommandExecutor.ExecuteAsync(ExecuteBranchPrimaryActionAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnCheckoutContextBranchAsync(GitBranch? branch) =>
        _asyncCommandExecutor.ExecuteAsync(() => CheckoutContextBranchAsync(branch));

    [RelayCommand(CanExecute = nameof(CanCreateBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateBranchAsync() => _asyncCommandExecutor.ExecuteAsync(CreateBranchAsync);

    [RelayCommand(CanExecute = nameof(CanRenameSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnRenameBranchAsync() => _asyncCommandExecutor.ExecuteAsync(RenameSelectedBranchAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnRenameContextBranchAsync(GitBranch? branch) =>
        _asyncCommandExecutor.ExecuteAsync(() => RenameContextBranchAsync(branch));

    [RelayCommand(CanExecute = nameof(CanEditSelectedBranchDescription), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditBranchDescriptionAsync() =>
        _asyncCommandExecutor.ExecuteAsync(EditSelectedBranchDescriptionAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnEditContextBranchDescriptionAsync(GitBranch? branch) =>
        _asyncCommandExecutor.ExecuteAsync(() => EditContextBranchDescriptionAsync(branch));

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedBranchDescription), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeleteBranchDescriptionAsync() =>
        _asyncCommandExecutor.ExecuteAsync(DeleteSelectedBranchDescriptionAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeleteContextBranchDescriptionAsync(GitBranch? branch) =>
        _asyncCommandExecutor.ExecuteAsync(() => DeleteContextBranchDescriptionAsync(branch));

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeleteBranchAsync() => _asyncCommandExecutor.ExecuteAsync(DeleteSelectedBranchAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeleteContextBranchAsync(GitBranch? branch) =>
        _asyncCommandExecutor.ExecuteAsync(() => DeleteContextBranchAsync(branch));

    [RelayCommand(CanExecute = nameof(CanCreateTag), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateTagAsync() => _asyncCommandExecutor.ExecuteAsync(CreateTagAsync);

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedTag), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeleteTagAsync() => _asyncCommandExecutor.ExecuteAsync(DeleteSelectedTagAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnDeleteContextTagAsync(GitTag? tag) =>
        _asyncCommandExecutor.ExecuteAsync(() => DeleteContextTagAsync(tag));

    [RelayCommand(CanExecute = nameof(CanCheckoutSelectedTag), FlowExceptionsToTaskScheduler = true)]
    private Task OnCheckoutTagAsync() => _asyncCommandExecutor.ExecuteAsync(CheckoutSelectedTagAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnCheckoutContextTagAsync(GitTag? tag) =>
        _asyncCommandExecutor.ExecuteAsync(() => CheckoutContextTagAsync(tag));

    [RelayCommand(CanExecute = nameof(CanCreateBranchFromSelectedTag), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateBranchFromTagAsync() =>
        _asyncCommandExecutor.ExecuteAsync(CreateBranchFromSelectedTagAsync);

    [RelayCommand(CanExecute = nameof(CanRunWhenIdle), FlowExceptionsToTaskScheduler = true)]
    private Task OnCreateBranchFromContextTagAsync(GitTag? tag) =>
        _asyncCommandExecutor.ExecuteAsync(() => CreateBranchFromContextTagAsync(tag));

    [RelayCommand(CanExecute = nameof(CanMergeSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnMergeBranchAsync(object? param) => 
        _asyncCommandExecutor.ExecuteAsync(() => MergeSelectedBranchAsync(param));

    [RelayCommand(CanExecute = nameof(CanMergeSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnSquashMergeBranchAsync() =>
        _asyncCommandExecutor.ExecuteAsync(SquashMergeSelectedBranchAsync);

    [RelayCommand(CanExecute = nameof(CanPrepareSelectedBranchSnapshot), FlowExceptionsToTaskScheduler = true)]
    private Task OnPrepareBranchSnapshotAsync() =>
        _asyncCommandExecutor.ExecuteAsync(PrepareSelectedBranchSnapshotAsync);

    [RelayCommand(CanExecute = nameof(CanMergeSelectedBranch), FlowExceptionsToTaskScheduler = true)]
    private Task OnRebaseBranchAsync() => _asyncCommandExecutor.ExecuteAsync(RebaseSelectedBranchAsync);

    [RelayCommand]
    private void OnOpenSelectedBranchChanges() => OpenSelectedBranchChanges();

    [RelayCommand]
    private void OnShowSelectedBranchCommits() => ShowSelectedBranchCommits(compareBothSides: false);

    [RelayCommand]
    private void OnCompareSelectedBranch() => ShowSelectedBranchCommits(compareBothSides: true);

    [RelayCommand]
    private void OnShowSelectedTagCommits() => ShowSelectedTagCommits();

    [RelayCommand]
    private void OnCopyText(string? text)
    {
        if (text is not null)
        {
            _clipboardService.SetText(text);
        }
    }

    public ObservableCollection<GitBranch> Branches { get; } = [];

    public ObservableCollection<GitBranch> RemoteBranches { get; } = [];

    public ObservableCollection<GitBranch> FilteredBranches { get; } = [];

    public ObservableCollection<GitTag> Tags { get; } = [];

    public ObservableCollection<GitTag> RemoteTags { get; } = [];

    public ObservableCollection<GitTag> FilteredTags { get; } = [];

    public ObservableCollection<BranchReflogDisplayItem> SelectedBranchHistoryEntries { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GitRemote> Remotes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRemoteUrl))]
    [NotifyPropertyChangedFor(nameof(HasSelectedRemote))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchSynchronizationTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTagRemoteStatusTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchSynchronizationText))]
    [NotifyPropertyChangedFor(nameof(SelectedTagRemoteStatusText))]
    public partial GitRemote? SelectedRemote { get; set; }

    partial void OnRemotesChanged(IReadOnlyList<GitRemote> value)
    {
        SyncSelectedBranchRemoteSettings();
    }

    partial void OnSelectedRemoteChanged(GitRemote? value)
    {
        _mainWindowViewModel.SelectRemote(value?.Name);
        SynchronizationSnapshot = null;
        _lastRepositoryFetch = null;
        _isLastRepositoryFetchLoaded = false;
        OnPropertyChanged(nameof(LastRepositoryFetchText));
        SyncSelectedBranchRemoteSettings();
        if (_hasRefreshed && !IsGitOperationRunning)
        {
            _ = RefreshSelectedRemoteAsync();
        }
    }

    public string SelectedRemoteUrl => SelectedRemote?.DisplayUrl ?? "";

    public string SelectedBranchName => SelectedBranch?.Name ?? "";
    public string SelectedTagName => SelectedTag?.Name ?? "";

    public bool HasSelectedRemote => SelectedRemote is not null;

    public bool CanUseRemoteSelector => IsSelectedBranchLocal
        && SynchronizationSnapshot is not null
        && !IsOperationProgressRunning;

    [ObservableProperty]
    public partial IReadOnlyList<BranchRemoteOption> BranchUpstreamRemoteOptions { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetSelectedBranchUpstream))]
    [NotifyCanExecuteChangedFor(nameof(SetBranchUpstreamCommand))]
    public partial BranchRemoteOption? SelectedBranchUpstreamRemoteOption { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<BranchRemoteOption> BranchPushRemoteOptions { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetSelectedBranchPushRemote))]
    [NotifyCanExecuteChangedFor(nameof(SetBranchPushRemoteCommand))]
    public partial BranchRemoteOption? SelectedBranchPushRemoteOption { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBranchSynchronization))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchSynchronizationText))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchIncomingStatusText))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchOutgoingStatusText))]
    [NotifyPropertyChangedFor(nameof(CanSetSelectedBranchUpstream))]
    [NotifyPropertyChangedFor(nameof(CanSetSelectedBranchPushRemote))]
    [NotifyCanExecuteChangedFor(nameof(SetBranchUpstreamCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetBranchPushRemoteCommand))]
    public partial SynchronizationSnapshot? SynchronizationSnapshot { get; private set; }

    partial void OnSelectedBranchUpstreamRemoteOptionChanged(BranchRemoteOption? value)
    {
        if (!_isSynchronizingBranchRemoteSettings && SetBranchUpstreamCommand.CanExecute(null))
        {
            SetBranchUpstreamCommand.TryExecute();
        }
    }
    
    partial void OnSelectedBranchPushRemoteOptionChanged(BranchRemoteOption? value)
    {
        if (!_isSynchronizingBranchRemoteSettings && SetBranchPushRemoteCommand.CanExecute(null))
        {
            SetBranchPushRemoteCommand.TryExecute();
        }
    }

    partial void OnSynchronizationSnapshotChanged(SynchronizationSnapshot? value)
    {
        SyncSelectedBranchRemoteSettings();
    }

    [ObservableProperty]
    public partial GitBranchDetails? SelectedBranchDetails { get; private set; }

    [ObservableProperty]
    public partial GitCommit? SelectedBranchCommit { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<GitWorktree>? SelectedBranchWorktrees { get; private set; }

    [ObservableProperty]
    public partial GitTagDetails? SelectedTagDetails { get; private set; }

    [ObservableProperty]
    public partial GitTagRelationDetails? SelectedTagRelationDetails { get; private set; }

    [ObservableProperty]
    public partial GitTagSignatureDetails? SelectedTagSignatureDetails { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<GitWorktree>? SelectedTagWorktrees { get; private set; }

    [ObservableProperty]
    public partial string NewBranchName { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReferenceListTitle))]
    [NotifyCanExecuteChangedFor(nameof(CreateReferenceCommand))]
    public partial ReferenceListKind ReferenceKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReferenceListTitle))]
    [NotifyPropertyChangedFor(nameof(BranchListTitle))]
    [NotifyPropertyChangedFor(nameof(TagListTitle))]
    public partial BranchListScope BranchScope { get; set; }

    partial void OnSelectedBranchDetailsChanged(GitBranchDetails? value)
    {
        NotifySelectedDetailsChanged();
    }

    partial void OnSelectedBranchCommitChanged(GitCommit? value)
    {
        NotifySelectedDetailsChanged();
    }

    partial void OnSelectedBranchWorktreesChanged(IReadOnlyList<GitWorktree>? value)
    {
        NotifySelectedDetailsChanged();
        UpdateCommandStates();
    }

    partial void OnSelectedTagDetailsChanged(GitTagDetails? value)
    {
        NotifySelectedDetailsChanged();
    }

    partial void OnSelectedTagRelationDetailsChanged(GitTagRelationDetails? value)
    {
        NotifySelectedDetailsChanged();
    }

    partial void OnSelectedTagSignatureDetailsChanged(GitTagSignatureDetails? value)
    {
        NotifySelectedDetailsChanged();
    }

    partial void OnSelectedTagWorktreesChanged(IReadOnlyList<GitWorktree>? value)
    {
        NotifySelectedDetailsChanged();
        UpdateCommandStates();
    }

    partial void OnNewBranchNameChanged(string value)
    {
        UpdateCommandStates();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyBranchFilter();
        ApplyTagFilter();
    }

    partial void OnReferenceKindChanged(ReferenceListKind value)
    {
        ApplyCurrentFilter();
        UpdateCommandStates();
        NotifyReferenceModeChanged();
    }

    partial void OnBranchScopeChanged(BranchListScope value)
    {
        ApplyBranchFilter();
        ApplyTagFilter();
    }

    public GitBranch? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (Equals(_selectedBranch, value))
            {
                return;
            }

            _selectedBranch = value;
            SyncSelectedBranchLists(value);
            SelectedBranchCommit = null;
            SelectedBranchDetails = null;
            SelectedBranchWorktrees = null;
            _selectedBranchReflogEntries = [];
            _isSelectedBranchHistoryLoaded = false;
            _showAllBranchHistory = false;
            SelectedBranchHistoryEntries.Clear();
            SyncSelectedBranchRemoteSettings();
            UpdateCommandStates();
            NotifySelectedDetailsChanged();
            OnPropertyChanged();
        }
    }

    public GitBranch? SelectedLocalBranch
    {
        get => _selectedLocalBranch;
        set
        {
            if (SetProperty(ref _selectedLocalBranch, value))
            {
                if (value is not null)
                {
                    SelectedBranch = value;
                }
                UpdateCommandStates();
            }
        }
    }

    public GitBranch? SelectedRemoteBranch
    {
        get => _selectedRemoteBranch;
        set
        {
            if (SetProperty(ref _selectedRemoteBranch, value))
            {
                if (value is not null)
                {
                    SelectedBranch = value;
                }
                UpdateCommandStates();
            }
        }
    }

    public GitTag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (Equals(_selectedTag, value))
            {
                return;
            }

            _selectedTag = value;
            SelectedTagDetails = null;
            SelectedTagSignatureDetails = null;
            _isSelectedTagSignatureLoaded = false;
            SelectedTagRelationDetails = null;
            _isSelectedTagRelationLoaded = false;
            SelectedTagWorktrees = null;
            UpdateCommandStates();
            NotifySelectedDetailsChanged();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string ProgressMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOperationProgressRunning))]
    [NotifyPropertyChangedFor(nameof(OperationProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(CanCancelRemoteOperation))]
    [NotifyPropertyChangedFor(nameof(CancelRemoteOperationVisibility))]
    [NotifyPropertyChangedFor(nameof(CanUseRemoteSelector))]
    public partial bool IsGitOperationRunning { get; private set; }

    [ObservableProperty]
    public partial bool IsMergeInProgress { get; private set; }

    [ObservableProperty]
    public partial bool IsRebaseInProgress { get; private set; }

    [ObservableProperty]
    public partial bool HasOperationInProgress { get; private set; }

    partial void OnProgressMessageChanged(string value)
    {
        PublishBranchesOperationState();
    }

    partial void OnIsGitOperationRunningChanged(bool value)
    {
        if (!value && _queuedReadOperationCount == 0)
        {
            ProgressMessage = "";
        }

        UpdateCommandStates();
        PublishBranchesOperationState();
    }

    partial void OnIsMergeInProgressChanged(bool value)
    {
        UpdateCommandStates();
    }

    partial void OnIsRebaseInProgressChanged(bool value)
    {
        UpdateCommandStates();
    }

    partial void OnHasOperationInProgressChanged(bool value)
    {
        UpdateCommandStates();
    }

    public bool IsOperationProgressRunning => IsGitOperationRunning || _queuedReadOperationCount > 0;

    public Visibility OperationProgressVisibility => IsOperationProgressRunning ? Visibility.Visible : Visibility.Collapsed;

    public bool CanCancelRemoteOperation => _remoteOperationCancellationTokenSource is { IsCancellationRequested: false };

    public Visibility CancelRemoteOperationVisibility => CanCancelRemoteOperation ? Visibility.Visible : Visibility.Collapsed;

    public bool HasSelectedBranch => ReferenceKind == ReferenceListKind.Branches && SelectedBranch is not null;

    public bool HasSelectedTag => ReferenceKind == ReferenceListKind.Tags && SelectedTag is not null;

    public bool IsSelectedBranchLocal => SelectedBranch?.IsLocal == true;

    public bool IsSelectedBranchRemote => SelectedBranch?.IsRemote == true;

    public bool HasSelectedBranchDescription => SelectedBranch?.HasConfigDescription == true;

    public bool CanEditSelectedBranchDescription => IsSelectedBranchLocal && !IsGitOperationRunning;

    public Visibility CanAddSelectedBranchDescriptionVisibility => !HasSelectedBranchDescription ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CanEditSelectedBranchDescriptionVisibility => HasSelectedBranchDescription ? Visibility.Visible : Visibility.Collapsed;

    public bool CanDeleteSelectedBranchDescription => HasSelectedBranchDescription
        && IsSelectedBranchLocal
        && !IsGitOperationRunning;

    public string SelectedBranchScopeText => SelectedBranch?.IsRemote == true
        ? _localizationService.GetString("RemoteReferenceLabel")
        : _localizationService.GetString("LocalReferenceLabel");

    public string SelectedBranchDescriptionSeparatorText => HasSelectedBranchDescription ? "  •  " : string.Empty;

    public string SelectedTagScopeText => SelectedTag?.IsRemote == true
        ? _localizationService.GetString("RemoteReferenceLabel")
        : _localizationService.GetString("LocalReferenceLabel");

    public string SelectedTagTypeText => SelectedTag?.IsAnnotated == true
        ? _localizationService.GetString("AnnotatedTagLabel")
        : _localizationService.GetString("LightweightTagLabel");

    public bool IsSelectedTagAnnotated => SelectedTag?.IsAnnotated == true;

    public GitCommit? SelectedReferenceCommit => ReferenceKind == ReferenceListKind.Branches
        ? SelectedBranchCommit
        : SelectedTagDetails?.TargetCommit;

    public string SelectedCommitTitle => SelectedReferenceCommit?.Title
        ?? (ReferenceKind == ReferenceListKind.Branches ? SelectedBranch?.LastCommitMessage : SelectedTag?.Subject)
        ?? "";

    public string SelectedCommitAuthor => SelectedReferenceCommit?.DisplayAuthor ?? "";

    public string SelectedCommitDate => SelectedReferenceCommit?.DisplayDate ?? "";

    public string SelectedCommitHash => SelectedReferenceCommit?.Hash ?? SelectedTag?.ObjectHash ?? "";

    public string SelectedCommitMessage => SelectedReferenceCommit?.Message ?? "";

    public BranchSynchronizationItem? SelectedBranchSynchronization
    {
        get
        {
            if (SelectedBranch is null)
            {
                return null;
            }

            if (SelectedBranch.IsLocal)
            {
                return SynchronizationSnapshot?.Branches.FirstOrDefault(
                    item => item.Name == SelectedBranch.Name);
            }

            if (RemoteBranchSynchronizationSnapshot is null
                || !string.Equals(
                    _remoteBranchSynchronizationRemoteName,
                    GetRemoteNameFromReference(SelectedBranch.Name),
                    StringComparison.Ordinal))
            {
                return null;
            }

            BranchSynchronizationItem? upstreamMatch = RemoteBranchSynchronizationSnapshot.Branches
                .Where(item => string.Equals(
                    item.UpstreamBranch,
                    SelectedBranch.Name,
                    StringComparison.Ordinal))
                .OrderByDescending(item => item.IsCurrent)
                .FirstOrDefault();
            if (upstreamMatch is not null)
            {
                return upstreamMatch;
            }

            string localName = GetLocalBranchName(SelectedBranch);
            return RemoteBranchSynchronizationSnapshot.Branches.FirstOrDefault(
                item => item.Name == localName);
        }
    }

    public string SelectedBranchSynchronizationTitle => SelectedBranch?.IsRemote == true
        ? _localizationService.GetString("CompareWithLocalBranchTitle")
        : _localizationService.GetString("BranchUpstreamAndRemoteStateTitle");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBranchSynchronization))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchSynchronizationText))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchIncomingStatusText))]
    [NotifyPropertyChangedFor(nameof(SelectedBranchOutgoingStatusText))]
    [NotifyPropertyChangedFor(nameof(IsSelectedRemoteBranchMissingLocal))]
    [NotifyPropertyChangedFor(nameof(SelectedRemoteBranchLocalName))]
    public partial SynchronizationSnapshot? RemoteBranchSynchronizationSnapshot { get; private set; }

    public bool CanSetSelectedBranchUpstream => IsSelectedBranchLocal
        && SelectedBranchUpstreamRemoteOption is not null
        && !IsGitOperationRunning
        && !string.Equals(
            SelectedBranchSynchronization?.HasUpstream == true
                ? SelectedBranchSynchronization.UpstreamRemoteName
                : "",
            SelectedBranchUpstreamRemoteOption.RemoteName ?? "",
            StringComparison.Ordinal);

    public bool CanSetSelectedBranchPushRemote => IsSelectedBranchLocal
        && SelectedBranchPushRemoteOption is not null
        && !IsGitOperationRunning
        && !string.Equals(
            SelectedBranchSynchronization?.ExplicitPushRemoteName ?? "",
            SelectedBranchPushRemoteOption.RemoteName ?? "",
            StringComparison.Ordinal);

    public string SelectedTagRemoteStatusTitle => SelectedTag?.IsRemote == true
        ? _localizationService.GetString("CompareWithLocalTagTitle")
        : _localizationService.GetString("TagStateAcrossRemotesTitle");

    public string LastRepositoryFetchText => !_isLastRepositoryFetchLoaded
        ? ""
        : _lastRepositoryFetch is null
            ? _localizationService.GetString("RepositoryFetchNotPerformed")
            : string.Format(
                _localizationService.GetString("RepositoryLastFetch"),
                _lastRepositoryFetch.Value.ToString("g", CultureInfo.CurrentCulture));

    public string SelectedBranchSynchronizationText
    {
        get
        {
            if (IsSelectedRemoteBranchMissingLocal)
            {
                return string.Format(
                    _localizationService.GetString("RemoteBranchHasNoLocalCopy"),
                    SelectedRemoteBranchLocalName);
            }

            BranchSynchronizationItem? item = SelectedBranchSynchronization;
            if (item is null) return _localizationService.GetString("BranchSynchronizationUnavailable");
            if (!item.IsPublishedToRemote)
            {
                return string.Format(
                    _localizationService.GetString("BranchNotPublishedToRemote"),
                    item.RemoteTrackingBranch);
            }
            if (SelectedBranch?.IsRemote == true)
            {
                if (item.IsDiverged)
                {
                    return string.Format(
                        _localizationService.GetString("RemoteBranchDivergedFromLocalSummary"),
                        item.BehindCount,
                        item.AheadCount);
                }

                if (item.HasIncomingCommits)
                {
                    return string.Format(
                        _localizationService.GetString("RemoteBranchAheadOfLocalSummary"),
                        item.BehindCount,
                        item.Name);
                }

                if (item.HasOutgoingCommits)
                {
                    return string.Format(
                        _localizationService.GetString("RemoteBranchBehindLocalSummary"),
                        item.AheadCount,
                        item.Name);
                }

                return string.Format(_localizationService.GetString("RemoteBranchMatchesLocalSummary"), item.Name);
            }

            if (item.IsDiverged) return string.Format(_localizationService.GetString("BranchDivergedSummary"), item.AheadCount, item.BehindCount);
            if (item.HasOutgoingCommits) return string.Format(_localizationService.GetString("BranchAheadSummary"), item.AheadCount, item.RemoteTrackingBranch);
            if (item.HasIncomingCommits) return string.Format(_localizationService.GetString("BranchBehindSummary"), item.BehindCount, item.RemoteTrackingBranch);
            return string.Format(_localizationService.GetString("BranchSynchronizedSummary"), item.RemoteTrackingBranch);
        }
    }

    public string SelectedBranchIncomingStatusText
    {
        get
        {
            BranchSynchronizationItem? item = SelectedBranchSynchronization;
            if (item is null)
            {
                return _localizationService.GetString("BranchSynchronizationUnavailable");
            }

            if (!item.IsPublishedToRemote)
            {
                return string.Format(
                    _localizationService.GetString("BranchIncomingReferenceMissing"),
                    item.RemoteTrackingBranch);
            }

            if (item.IsDiverged)
            {
                return string.Format(
                    _localizationService.GetString("BranchIncomingDiverged"),
                    item.RemoteTrackingBranch,
                    item.AheadCount,
                    item.BehindCount);
            }

            if (item.HasIncomingCommits)
            {
                return string.Format(
                    _localizationService.GetString("BranchIncomingAvailable"),
                    item.RemoteTrackingBranch,
                    item.BehindCount);
            }

            if (item.HasOutgoingCommits)
            {
                return string.Format(
                    _localizationService.GetString("BranchIncomingLocalAhead"),
                    item.RemoteTrackingBranch,
                    item.AheadCount);
            }

            return string.Format(
                _localizationService.GetString("BranchIncomingSynchronized"),
                item.RemoteTrackingBranch);
        }
    }

    public string SelectedBranchOutgoingStatusText
    {
        get
        {
            BranchSynchronizationItem? item = SelectedBranchSynchronization;
            if (item is null || string.IsNullOrWhiteSpace(item.PushTrackingBranch))
            {
                return _localizationService.GetString("BranchSynchronizationUnavailable");
            }

            if (!item.IsPublishedToPushRemote)
            {
                return string.Format(
                    _localizationService.GetString("BranchOutgoingNotPublished"),
                    item.PushTrackingBranch);
            }

            if (item.PushAheadCount > 0 && item.PushBehindCount > 0)
            {
                return string.Format(
                    _localizationService.GetString("BranchOutgoingDiverged"),
                    item.PushTrackingBranch,
                    item.PushAheadCount,
                    item.PushBehindCount);
            }

            if (item.PushAheadCount > 0)
            {
                return string.Format(
                    _localizationService.GetString("BranchOutgoingAvailable"),
                    item.PushTrackingBranch,
                    item.PushAheadCount);
            }

            if (item.PushBehindCount > 0)
            {
                return string.Format(
                    _localizationService.GetString("BranchOutgoingRemoteAhead"),
                    item.PushTrackingBranch,
                    item.PushBehindCount);
            }

            return string.Format(
                _localizationService.GetString("BranchOutgoingSynchronized"),
                item.PushTrackingBranch);
        }
    }

    public string SelectedRemoteBranchLocalName => SelectedBranch?.IsRemote == true
        ? SelectedBranchSynchronization?.Name ?? GetLocalBranchName(SelectedBranch)
        : GetLocalBranchName(SelectedBranch);

    public bool IsSelectedRemoteBranchMissingLocal => SelectedBranch?.IsRemote == true
        && !string.IsNullOrWhiteSpace(SelectedRemoteBranchLocalName)
        && (RemoteBranchSynchronizationSnapshot is not null
            ? SelectedBranchSynchronization is null
            : Branches.All(item => !item.Name.Equals(
                SelectedRemoteBranchLocalName,
                StringComparison.Ordinal)));

    public string SelectedBranchRelationText
    {
        get
        {
            if (SelectedBranchDetails is null) return _localizationService.GetString("BranchComparisonNotLoadedSummary");
            if (SelectedBranch?.IsCurrent == true) return _localizationService.GetString("SelectedBranchIsCurrentSummary");
            if (SelectedBranchDetails.CommitsOnlyInSelected == 0 && SelectedBranchDetails.CommitsOnlyInCurrent == 0)
                return _localizationService.GetString("BranchesPointToSameCommitSummary");
            if (SelectedBranchDetails.IsMergedIntoCurrent)
                return string.Format(_localizationService.GetString("BranchMergedIntoCurrentSummary"), SelectedBranchDetails.CommitsOnlyInCurrent);
            return string.Format(
                _localizationService.GetString("BranchRelationSummary"),
                SelectedBranchDetails.CommitsOnlyInSelected,
                SelectedBranchDetails.CommitsOnlyInCurrent);
        }
    }

    public string SelectedBranchDiffText => SelectedBranchDetails is null
        ? ""
        : string.Format(_localizationService.GetString("BranchDiffSummary"), SelectedBranchDetails.ChangedFiles, SelectedBranchDetails.DiffStat.AddedLines, SelectedBranchDetails.DiffStat.RemovedLines);

    public string SelectedBranchMergeBaseText
    {
        get
        {
            GitCommit? commit = SelectedBranchDetails?.MergeBaseCommit;
            return commit is null
                ? _localizationService.GetString("CommonCommitUnavailable")
                : string.Format(
                    _localizationService.GetString("CommonCommitSummary"),
                    commit.ShortHash,
                    commit.Title,
                    commit.DisplayAuthor,
                    commit.DisplayDate);
        }
    }

    public string SelectedBranchMergeCapabilityText
    {
        get
        {
            if (SelectedBranchDetails is null)
            {
                return "";
            }

            if (SelectedBranch?.IsCurrent == true ||
                SelectedBranchDetails.CommitsOnlyInSelected == 0 && SelectedBranchDetails.CommitsOnlyInCurrent == 0)
            {
                return _localizationService.GetString("BranchMergeSameCommit");
            }

            if (SelectedBranchDetails.IsMergedIntoCurrent)
            {
                return _localizationService.GetString("BranchMergeAlreadyContained");
            }

            if (SelectedBranchDetails.CanFastForwardCurrent)
            {
                return _localizationService.GetString("BranchMergeFastForwardAvailable");
            }

            return SelectedBranchDetails.MergeBaseCommit is null
                ? _localizationService.GetString("BranchMergeUnrelatedHistories")
                : _localizationService.GetString("BranchMergeCommitRequired");
        }
    }

    public bool CanOpenSelectedBranchChanges => SelectedBranchDetails?.ChangedFiles > 0;

    public bool CanShowSelectedBranchCommits => SelectedBranchDetails?.CommitsOnlyInSelected > 0;

    public bool CanCompareSelectedBranch => SelectedBranchDetails is not null &&
        (SelectedBranchDetails.CommitsOnlyInSelected > 0 || SelectedBranchDetails.CommitsOnlyInCurrent > 0);

    public bool CanOpenLastCommitBranchChanges => SelectedBranch?.ShortCommitHash.Length > 0;
    public bool HasSelectedBranchWorktree => SelectedBranchWorktrees?.Count > 0;

    public bool IsSelectedBranchInOtherWorktree => SelectedBranch?.IsCurrent == false && HasSelectedBranchWorktree;

    public bool CanCreateSelectedBranchWorktree => SelectedBranch is { IsCurrent: false } && !HasSelectedBranchWorktree;

    public bool CanOpenSelectedBranchWorktree => SelectedBranchWorktrees?.Any(item => !item.IsCurrent && !item.IsBare && !item.IsPrunable) == true;

    public string BranchPrimaryActionText => IsSelectedBranchInOtherWorktree
        ? _localizationService.GetString("OpenWorktreeButtonText")
        : _localizationService.GetString("CheckoutBranchButtonText");

    public string SelectedBranchWorktreePath => SelectedBranchWorktrees is null
        ? _localizationService.GetString("BranchWorktreeNotLoadedSummary")
        : SelectedBranchWorktrees.FirstOrDefault()?.Path
            ?? _localizationService.GetString("NoBranchWorktreesSummary");

    public string SelectedBranchHistorySummary
    {
        get
        {
            if (!_isSelectedBranchHistoryLoaded)
            {
                return _localizationService.GetString("BranchHistoryNotLoadedSummary");
            }

            GitReflogEntry? latestEntry = _selectedBranchReflogEntries.FirstOrDefault();
            if (latestEntry is null)
            {
                return _localizationService.GetString("BranchHistoryEmptySummary");
            }

            return string.Format(
                _localizationService.GetString("BranchHistoryLatestSummary"),
                FormatReflogDate(latestEntry.OccurredAt),
                GetReflogActionText(latestEntry.Subject));
        }
    }

    public string SelectedBranchHistoryAvailabilityText
    {
        get
        {
            GitReflogEntry? earliestEntry = _selectedBranchReflogEntries.LastOrDefault();
            if (earliestEntry is null)
            {
                return _localizationService.GetString("BranchHistoryNoAvailableEntries");
            }

            string resourceKey = earliestEntry.IsPossibleCreation
                ? "BranchHistoryPossibleCreation"
                : "BranchHistoryEarliestAvailable";
            return string.Format(
                _localizationService.GetString(resourceKey),
                FormatReflogDate(earliestEntry.OccurredAt));
        }
    }

    public bool HasMoreBranchHistoryEntries => _selectedBranchReflogEntries.Count > BranchHistoryPreviewCount;

    public string BranchHistoryToggleText => _showAllBranchHistory
        ? _localizationService.GetString("CollapseBranchHistoryButtonText")
        : string.Format(
            _localizationService.GetString("ShowAllBranchHistoryButtonText"),
            _selectedBranchReflogEntries.Count);

    public bool HasSelectedTagWorktrees => SelectedTagWorktrees?.Count > 0;

    public string SelectedTagWorktreeText => SelectedTagWorktrees is null
        ? _localizationService.GetString("TagWorktreeNotLoadedSummary")
        : SelectedTagWorktrees.Count > 0
            ? string.Join(Environment.NewLine, SelectedTagWorktrees.Select(item => $"{item.DisplayName} — {item.Path}"))
            : _localizationService.GetString("NoTagWorktreesSummary");

    public string SelectedTaggerText => SelectedTagDetails is null || string.IsNullOrWhiteSpace(SelectedTagDetails.TaggerName)
        ? SelectedTagDetails?.TargetCommit?.DisplayAuthor ?? ""
        : string.IsNullOrWhiteSpace(SelectedTagDetails.TaggerEmail)
            ? SelectedTagDetails.TaggerName
            : $"{SelectedTagDetails.TaggerName} <{SelectedTagDetails.TaggerEmail}>";

    public string SelectedTaggerDate => !string.IsNullOrWhiteSpace(SelectedTagDetails?.TaggerDate)
        ? FormatDisplayDate(SelectedTagDetails.TaggerDate)
        : SelectedTagDetails?.TargetCommit?.DisplayDate ?? SelectedTag?.CreatedDate?.ToString("g") ?? "";

    public string SelectedTagMessage => !string.IsNullOrWhiteSpace(SelectedTagDetails?.Message)
        ? SelectedTagDetails.Message
        : SelectedTagDetails?.TargetCommit?.Title ?? SelectedTag?.Subject ?? "";

    public string SelectedTagTargetType => SelectedTagDetails?.TargetObjectType
        ?? _localizationService.GetString("TagTargetUnavailable");

    public string SelectedTagTargetCommitText
    {
        get
        {
            GitCommit? commit = SelectedTagDetails?.TargetCommit;
            return commit is null
                ? _localizationService.GetString("TagTargetCommitUnavailable")
                : string.Format(
                    _localizationService.GetString("TagTargetCommitSummary"),
                    commit.ShortHash,
                    commit.Title,
                    commit.DisplayAuthor,
                    commit.DisplayDate);
        }
    }

    public string SelectedTagSignatureSummary
    {
        get
        {
            if (!_isSelectedTagSignatureLoaded)
            {
                return _localizationService.GetString("TagSignatureNotLoadedSummary");
            }

            return SelectedTagSignatureDetails?.Status switch
            {
                GitTagSignatureStatus.NotSigned => _localizationService.GetString("TagSignatureNotSignedSummary"),
                GitTagSignatureStatus.Valid => _localizationService.GetString("TagSignatureValidSummary"),
                GitTagSignatureStatus.Invalid => _localizationService.GetString("TagSignatureInvalidSummary"),
                GitTagSignatureStatus.UnknownKey => _localizationService.GetString("TagSignatureUnknownKeySummary"),
                _ => _localizationService.GetString("TagSignatureUnavailableSummary")
            };
        }
    }

    public string SelectedTagSignatureDetailsText
    {
        get
        {
            GitTagSignatureDetails? details = SelectedTagSignatureDetails;
            if (details is null)
            {
                return _localizationService.GetString("TagSignatureObjectUnavailable");
            }

            if (details.Status == GitTagSignatureStatus.NotSigned)
            {
                return _localizationService.GetString("TagSignatureNotSignedDetails");
            }

            List<string> lines =
            [
                string.Format(
                    _localizationService.GetString("TagSignatureTypeLine"),
                    GetSignatureTypeText(details.SignatureType))
            ];
            if (!string.IsNullOrWhiteSpace(details.Signer))
            {
                lines.Add(string.Format(_localizationService.GetString("TagSignatureSignerLine"), details.Signer));
            }
            if (!string.IsNullOrWhiteSpace(details.KeyId))
            {
                lines.Add(string.Format(_localizationService.GetString("TagSignatureKeyLine"), details.KeyId));
            }
            if (!string.IsNullOrWhiteSpace(details.Fingerprint))
            {
                lines.Add(string.Format(_localizationService.GetString("TagSignatureFingerprintLine"), details.Fingerprint));
            }
            if (!string.IsNullOrWhiteSpace(details.Diagnostic)
                && details.Status != GitTagSignatureStatus.Valid)
            {
                lines.Add(string.Format(_localizationService.GetString("TagSignatureDiagnosticLine"), details.Diagnostic));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string SelectedTagRelationText
    {
        get
        {
            if (!_isSelectedTagRelationLoaded)
            {
                return _localizationService.GetString("TagRelationNotLoadedSummary");
            }

            GitTagRelationDetails? details = SelectedTagRelationDetails;
            if (details is null)
            {
                return _localizationService.GetString("TagRelationUnavailable");
            }

            if (details.CommitsOnlyInCurrent == 0 && details.CommitsOnlyInTag == 0)
            {
                return _localizationService.GetString("HeadExactlyAtTag");
            }

            if (details.MergeBaseCommit is null)
            {
                return _localizationService.GetString("TagHistoryUnrelated");
            }

            if (details.CommitsOnlyInTag == 0)
            {
                return string.Format(_localizationService.GetString("HeadAheadOfTag"), details.CommitsOnlyInCurrent);
            }

            if (details.CommitsOnlyInCurrent == 0)
            {
                return string.Format(_localizationService.GetString("TagAheadOfHead"), details.CommitsOnlyInTag);
            }

            return string.Format(
                _localizationService.GetString("TagHistoryDiverged"),
                details.CommitsOnlyInCurrent,
                details.CommitsOnlyInTag);
        }
    }

    public string SelectedTagMergeBaseText
    {
        get
        {
            GitCommit? commit = SelectedTagRelationDetails?.MergeBaseCommit;
            return commit is null
                ? _localizationService.GetString("CommonCommitUnavailable")
                : string.Format(
                    _localizationService.GetString("CommonCommitSummary"),
                    commit.ShortHash,
                    commit.Title,
                    commit.DisplayAuthor,
                    commit.DisplayDate);
        }
    }

    public string SelectedTagContainingBranchesText => SelectedTagRelationDetails is null
        ? _localizationService.GetString("TagContainingBranchesUnavailable")
        : SelectedTagRelationDetails.ContainingLocalBranches.Count == 0
            ? _localizationService.GetString("NoLocalBranchesContainTag")
            : string.Join(Environment.NewLine, SelectedTagRelationDetails.ContainingLocalBranches);

    public bool CanShowSelectedTagCommits => SelectedTagRelationDetails is not null
        && (SelectedTagRelationDetails.CommitsOnlyInCurrent > 0 || SelectedTagRelationDetails.CommitsOnlyInTag > 0);

    public string SelectedTagRemoteStatusText
    {
        get
        {
            if (SelectedTag is null)
            {
                return _localizationService.GetString("TagRemoteStatusUnavailable");
            }

            if (SelectedTag.IsRemote)
            {
                GitTag? localTag = FindLocalTag(SelectedTag);
                if (localTag is null)
                {
                    return string.Format(
                        _localizationService.GetString("LocalTagMissing"),
                        SelectedTag.RemoteTagName);
                }

                return localTag.ReferenceObjectHash.Equals(
                    SelectedTag.ReferenceObjectHash,
                    StringComparison.OrdinalIgnoreCase)
                    ? string.Format(
                        _localizationService.GetString("RemoteTagMatchesLocal"),
                        localTag.Name)
                    : string.Format(
                        _localizationService.GetString("RemoteTagConflictsWithLocal"),
                        localTag.Name);
            }

            if (_remoteTagLoadAttempts.Count == 0)
            {
                return _localizationService.GetString("TagRemoteStatusUnavailable");
            }

            IReadOnlyList<GitTag> remoteTags = GetCachedRemoteTags()
                .Where(tag => tag.RemoteTagName == SelectedTag.Name)
                .ToList();
            if (remoteTags.Count == 0)
            {
                return _localizationService.GetString("TagOnlyLocalAcrossRemotes");
            }

            string matchingRemotes = string.Join(
                ", ",
                remoteTags
                    .Where(tag => tag.ReferenceObjectHash.Equals(
                        SelectedTag.ReferenceObjectHash,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(tag => tag.RemoteName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            string conflictingRemotes = string.Join(
                ", ",
                remoteTags
                    .Where(tag => !tag.ReferenceObjectHash.Equals(
                        SelectedTag.ReferenceObjectHash,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(tag => tag.RemoteName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(conflictingRemotes))
            {
                return string.Format(
                    _localizationService.GetString("TagMatchesRemotes"),
                    matchingRemotes);
            }

            if (string.IsNullOrWhiteSpace(matchingRemotes))
            {
                return string.Format(
                    _localizationService.GetString("TagConflictsWithRemotes"),
                    conflictingRemotes);
            }

            return string.Format(
                _localizationService.GetString("TagMixedRemoteState"),
                matchingRemotes,
                conflictingRemotes);
        }
    }

    public bool IsSelectedRemoteTagMissingLocal => SelectedTag?.IsRemote == true
        && FindLocalTag(SelectedTag) is null;

    [ObservableProperty]
    public partial bool HasNoBranches { get; private set; }

    [ObservableProperty]
    public partial bool HasNoTags { get; private set; }

    public string ReferenceListTitle => ReferenceKind == ReferenceListKind.Tags
        ? TagListTitle
        : BranchListTitle;

    public string BranchListTitle => BranchScope switch
    {
        BranchListScope.Remote => _localizationService.GetString("BranchScopeRemoteTitle"),
        BranchListScope.All => _localizationService.GetString("BranchScopeAllTitle"),
        _ => _localizationService.GetString("BranchScopeLocalTitle")
    };

    public string TagListTitle => BranchScope switch
    {
        BranchListScope.Remote => _localizationService.GetString("TagScopeRemoteTitle"),
        BranchListScope.All => _localizationService.GetString("TagScopeAllTitle"),
        _ => _localizationService.GetString("TagScopeLocalTitle")
    };

    public Visibility BranchesModeVisibility => ReferenceKind == ReferenceListKind.Branches ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TagsModeVisibility => ReferenceKind == ReferenceListKind.Tags ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ReferencesVisible => Branches.Count == 0 && RemoteBranches.Count == 0 && Tags.Count == 0 && RemoteTags.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility BranchesVisible => Branches.Count == 0 && RemoteBranches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TagsVisible => Tags.Count == 0 && RemoteTags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FilteredBranchesVisible => ReferenceKind == ReferenceListKind.Branches && FilteredBranches.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility FilteredTagsVisible => ReferenceKind == ReferenceListKind.Tags && FilteredTags.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool HasFilteredBranches => ReferenceKind == ReferenceListKind.Branches && FilteredBranches.Count > 0;

    public bool HasFilteredTags => ReferenceKind == ReferenceListKind.Tags && FilteredTags.Count > 0;

    public bool HasNoFilteredBranches => ReferenceKind == ReferenceListKind.Branches && !HasNoBranches && FilteredBranches.Count == 0;

    public bool HasNoFilteredTags => ReferenceKind == ReferenceListKind.Tags && !HasNoTags && FilteredTags.Count == 0;

    public bool HasNoBranchesNotice => ReferenceKind == ReferenceListKind.Branches && HasNoBranches;

    public bool HasNoTagsNotice => ReferenceKind == ReferenceListKind.Tags && HasNoTags;

    public bool CanCheckoutSelectedBranch => ReferenceKind == ReferenceListKind.Branches
        && SelectedBranch is not null
        && !SelectedBranch.IsCurrent
        && !IsGitOperationRunning;

    public bool CanDeleteSelectedBranch => ReferenceKind == ReferenceListKind.Branches
        && SelectedBranch is not null
        && !SelectedBranch.IsCurrent
        && !IsGitOperationRunning;

    public bool CanRenameSelectedBranch => ReferenceKind == ReferenceListKind.Branches
        && SelectedBranch is not null
        && SelectedBranch.IsLocal
        && !IsGitOperationRunning;

    public bool CanMergeSelectedBranch => ReferenceKind == ReferenceListKind.Branches
        && SelectedBranch is not null
        && !SelectedBranch.IsCurrent
        && !HasOperationInProgress
        && !IsGitOperationRunning;

    public bool CanPrepareSelectedBranchSnapshot => CanMergeSelectedBranch;

    public bool CanCreateBranch => ReferenceKind == ReferenceListKind.Branches && !IsGitOperationRunning;

    public bool CanCreateTag => ReferenceKind == ReferenceListKind.Tags && !IsGitOperationRunning;

    public bool CanCreateReference => ReferenceKind == ReferenceListKind.Tags ? CanCreateTag : CanCreateBranch;

    public bool CanFetchSelectedBranch => ReferenceKind == ReferenceListKind.Branches
        && IsSelectedRemoteBranchMissingLocal
        && !IsGitOperationRunning;

    public bool CanFetchSelectedTag => ReferenceKind == ReferenceListKind.Tags
        && IsSelectedRemoteTagMissingLocal
        && SelectedRemote is not null
        && !IsGitOperationRunning;

    public bool CanDeleteSelectedTag => ReferenceKind == ReferenceListKind.Tags
        && SelectedTag is not null
        && !IsGitOperationRunning;

    public bool CanCheckoutSelectedTag => ReferenceKind == ReferenceListKind.Tags
        && SelectedTag is not null
        && !IsGitOperationRunning;

    public bool CanCreateBranchFromSelectedTag => ReferenceKind == ReferenceListKind.Tags
        && SelectedTag is not null
        && !IsGitOperationRunning;

    public async Task RefreshBranchesLocalAsync()
    {
        await RunGitOperationAsync(
            _localizationService.GetString("LoadingBranchesOverviewProgress"),
            async () =>
            {
                await RefreshBranchesCoreAsync(refreshLoadedTags: true);
            });
    }

    public async Task RefreshSelectedRemoteAsync()
    {
        await RefreshBranchesLocalAsync();
        if (ReferenceKind == ReferenceListKind.Tags)
        {
            await EnsureTagsLoadedAsync();
            if (BranchScope == BranchListScope.Remote)
            {
                await EnsureRemoteTagsLoadedAsync();
            }
        }
    }

    public async Task RefreshBranchesAsync()
    {
        await RunRemoteOperationAsync(
            _localizationService.GetString("RefreshingAllRemotesProgress"),
            RefreshBranchesFromRemoteCoreAsync);
    }

    public async Task EnsureTagsLoadedAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        if (!_areLocalTagsLoaded)
        {
            await RunGitOperationAsync(
                _localizationService.GetString("LoadingLocalTagsProgress"),
                async () =>
                {
                    Task<IReadOnlyList<GitTag>> tagsTask = _gitService.GetLocalTagsAsync(repository);
                    Task<string?> headTask = _gitService.Tags.GetHeadCommitHashAsync(repository);
                    await Task.WhenAll(tagsTask, headTask);
                    _areLocalTagsLoaded = true;
                    _tagHeadCommitHash = await headTask;
                    ReplaceLocalTags(await tagsTask, _tagHeadCommitHash);
                });
        }

    }

    public async Task EnsureRemoteTagsLoadedAsync()
    {
        await EnsureTagsLoadedAsync();

        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null || Remotes.Count == 0)
        {
            return;
        }

        IReadOnlyList<GitRemote> remotesToLoad = Remotes
            .Where(remote => !_remoteTagLoadAttempts.Contains(remote.Name))
            .ToList();
        ReplaceRemoteTags(GetCachedRemoteTags(), _tagHeadCommitHash);
        if (remotesToLoad.Count == 0)
        {
            return;
        }

        await RunRemoteOperationAsync(
            _localizationService.GetString("LoadingAllRemoteTagsProgress"),
            async cancellationToken =>
            {
                List<string> failedRemotes = [];
                foreach (GitRemote remote in remotesToLoad)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        IReadOnlyList<GitTag> remoteTags = await _gitService.Remotes.GetRemoteTagsAsync(
                            repository,
                            remote,
                            cancellationToken);
                        _remoteTagCache[remote.Name] = remoteTags;
                    }
                    catch (Exception exception) when (exception is GitRemoteOperationException or GitCommandException)
                    {
                        failedRemotes.Add(remote.Name);
                    }

                    _remoteTagLoadAttempts.Add(remote.Name);
                }

                _tagHeadCommitHash ??= await _gitService.Tags.GetHeadCommitHashAsync(repository);
                ReplaceRemoteTags(GetCachedRemoteTags(), _tagHeadCommitHash);
                if (failedRemotes.Count > 0)
                {
                    ShowWarning(string.Format(
                        _localizationService.GetString("RemoteTagsPartialFailure"),
                        string.Join(", ", failedRemotes)));
                }
            });
    }

    public Task EnsureSelectedBranchCommitLoadedAsync()
    {
        if (SelectedBranch is null || SelectedBranchCommit is not null)
        {
            return Task.CompletedTask;
        }

        GitBranch branch = SelectedBranch;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingBranchCommitProgress"), branch.Name),
            () => LoadSelectedBranchCommitAsync(branch));
    }

    public Task EnsureSelectedBranchComparisonLoadedAsync()
    {
        if (SelectedBranch is null || SelectedBranchDetails is not null)
        {
            return Task.CompletedTask;
        }

        GitBranch branch = SelectedBranch;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingBranchComparisonProgress"), branch.Name),
            () => LoadSelectedBranchComparisonAsync(branch));
    }

    public Task EnsureSelectedBranchWorktreesLoadedAsync()
    {
        if (SelectedBranch is null || SelectedBranchWorktrees is not null)
        {
            return Task.CompletedTask;
        }

        GitBranch branch = SelectedBranch;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingBranchWorktreesProgress"), branch.Name),
            () => LoadSelectedBranchWorktreesAsync(branch));
    }

    public Task EnsureSelectedBranchHistoryLoadedAsync()
    {
        if (SelectedBranch?.IsLocal != true || _isSelectedBranchHistoryLoaded)
        {
            return Task.CompletedTask;
        }

        GitBranch branch = SelectedBranch;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingBranchHistoryProgress"), branch.Name),
            () => LoadSelectedBranchHistoryAsync(branch));
    }

    public Task EnsureSelectedTagDetailsLoadedAsync()
    {
        if (SelectedTag is null || SelectedTagDetails is not null)
        {
            return Task.CompletedTask;
        }

        GitTag tag = SelectedTag;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingTagDetailsProgress"), tag.Name),
            () => LoadSelectedTagDetailsAsync(tag));
    }

    public Task EnsureSelectedTagSignatureLoadedAsync()
    {
        if (SelectedTag?.IsAnnotated != true || _isSelectedTagSignatureLoaded)
        {
            return Task.CompletedTask;
        }

        GitTag tag = SelectedTag;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingTagSignatureProgress"), tag.Name),
            () => LoadSelectedTagSignatureAsync(tag));
    }

    public Task EnsureSelectedTagRelationLoadedAsync()
    {
        if (SelectedTag is null || _isSelectedTagRelationLoaded)
        {
            return Task.CompletedTask;
        }

        GitTag tag = SelectedTag;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingTagRelationProgress"), tag.Name),
            () => LoadSelectedTagRelationAsync(tag));
    }

    public Task EnsureSelectedTagWorktreesLoadedAsync()
    {
        if (SelectedTag is null || SelectedTagWorktrees is not null)
        {
            return Task.CompletedTask;
        }

        GitTag tag = SelectedTag;
        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingTagWorktreesProgress"), tag.Name),
            () => LoadSelectedTagWorktreesAsync(tag));
    }

    public Task EnsureBranchSynchronizationLoadedAsync()
    {
        bool branchSnapshotLoaded = SelectedBranch?.IsLocal == true
            ? SynchronizationSnapshot is not null
            : SelectedBranch?.IsRemote == true
                && RemoteBranchSynchronizationSnapshot is not null
                && string.Equals(
                    _remoteBranchSynchronizationRemoteName,
                    GetRemoteNameFromReference(SelectedBranch.Name),
                    StringComparison.Ordinal);
        if (branchSnapshotLoaded && _isLastRepositoryFetchLoaded)
        {
            return Task.CompletedTask;
        }

        if (_mainWindowViewModel.CurrentRepository is null ||
            SelectedBranch is null)
        {
            return Task.CompletedTask;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        GitBranch branch = SelectedBranch;
        GitRemote? remote = branch.IsLocal
            ? SelectedRemote
            : FindRemote(GetRemoteNameFromReference(branch.Name));
        if (remote is null)
        {
            return Task.CompletedTask;
        }

        return RunQueuedReadOperationAsync(
            string.Format(_localizationService.GetString("LoadingBranchSynchronizationProgress"), remote.Name),
            async () =>
            {
                Task<DateTimeOffset?> lastFetchTask = _gitService.GetLastFetchTimeAsync(repository);
                Task<SynchronizationSnapshot> snapshotTask = branch.IsLocal
                    ? _gitService.Remotes.GetLocalConfiguredSynchronizationSnapshotAsync(repository, remote, [])
                    : _gitService.Remotes.GetLocalSynchronizationSnapshotAsync(repository, remote, []);
                await Task.WhenAll(lastFetchTask, snapshotTask);

                if (_mainWindowViewModel.CurrentRepository?.Path == repository.Path)
                {
                    _lastRepositoryFetch = await lastFetchTask;
                    _isLastRepositoryFetchLoaded = true;
                    OnPropertyChanged(nameof(LastRepositoryFetchText));
                    if (branch.IsLocal)
                    {
                        SynchronizationSnapshot = await snapshotTask;
                    }
                    else if (SelectedBranch?.IsRemote == true
                        && string.Equals(
                            GetRemoteNameFromReference(SelectedBranch.Name),
                            remote.Name,
                            StringComparison.Ordinal))
                    {
                        _remoteBranchSynchronizationRemoteName = remote.Name;
                        RemoteBranchSynchronizationSnapshot = await snapshotTask;
                    }
                }
            });
    }

    private async Task SetSelectedBranchUpstreamAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitBranch? branch = SelectedBranch;
        BranchRemoteOption? upstreamRemoteOption = SelectedBranchUpstreamRemoteOption;
        GitRemote? comparisonRemote = SelectedRemote;
        if (repository is null
            || branch?.IsLocal != true
            || upstreamRemoteOption is null
            || comparisonRemote is null)
        {
            return;
        }

        string branchName = branch.Name;
        string? upstreamRemoteName = upstreamRemoteOption.RemoteName;
        await ExecuteBranchOperationAsync(
            string.Format(
                _localizationService.GetString("SettingBranchUpstreamProgress"),
                branchName),
            () => string.IsNullOrWhiteSpace(upstreamRemoteName)
                ? _gitService.Configuration.UnsetBranchUpstreamAsync(repository, branchName)
                : _gitService.Configuration.SetBranchUpstreamAsync(repository, branchName, upstreamRemoteName),
            async () =>
            {
                SynchronizationSnapshot = await _gitService.Remotes.GetLocalConfiguredSynchronizationSnapshotAsync(
                    repository,
                    comparisonRemote,
                    []);
                string configuredUpstream = SynchronizationSnapshot.Branches
                    .FirstOrDefault(item => item.Name == branchName)
                    ?.UpstreamBranch
                    ?? upstreamRemoteName
                    ?? "";
                string successMessage = string.IsNullOrWhiteSpace(upstreamRemoteName)
                    ? string.Format(
                        _localizationService.GetString("BranchUpstreamResetSucceeded"),
                        branchName)
                    : string.Format(
                        _localizationService.GetString("BranchUpstreamSetSucceeded"),
                        branchName,
                        configuredUpstream);
                ShowSuccess(successMessage);
            });
        SyncSelectedBranchRemoteSettings();
    }

    private async Task SetSelectedBranchPushRemoteAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitBranch? branch = SelectedBranch;
        BranchRemoteOption? pushRemoteOption = SelectedBranchPushRemoteOption;
        GitRemote? comparisonRemote = SelectedRemote;
        if (repository is null
            || branch?.IsLocal != true
            || pushRemoteOption is null
            || comparisonRemote is null)
        {
            return;
        }

        string branchName = branch.Name;
        await ExecuteBranchOperationAsync(
            string.Format(
                _localizationService.GetString("SettingBranchPushRemoteProgress"),
                branchName),
            () => _gitService.Configuration.SetBranchPushRemoteAsync(
                repository,
                branchName,
                pushRemoteOption.RemoteName),
            async () =>
            {
                SynchronizationSnapshot = await _gitService.Remotes.GetLocalConfiguredSynchronizationSnapshotAsync(
                    repository,
                    comparisonRemote,
                    []);
                string successMessage = string.IsNullOrWhiteSpace(pushRemoteOption.RemoteName)
                    ? string.Format(
                        _localizationService.GetString("BranchPushRemoteResetSucceeded"),
                        branchName)
                    : string.Format(
                        _localizationService.GetString("BranchPushRemoteSetSucceeded"),
                        branchName,
                        pushRemoteOption.RemoteName);
                ShowSuccess(successMessage);
            });
        SyncSelectedBranchRemoteSettings();
    }

    private async Task RefreshBranchesFromRemoteCoreAsync(CancellationToken cancellationToken)
    {
        ClearResultMessages();

        if (_mainWindowViewModel.CurrentRepository is null)
        {
            await RefreshBranchesCoreAsync();
            return;
        }

        try
        {
            RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
            IReadOnlyList<GitRemote> remotes = await _gitService.GetRemotesAsync(
                repository,
                cancellationToken);
            List<string> failedRemotes = [];
            foreach (GitRemote remote in remotes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool remoteFailed = false;
                try
                {
                    await _gitService.Remotes.FetchBranchesAsync(repository, remote, cancellationToken);
                }
                catch (Exception exception) when (exception is GitRemoteOperationException or GitCommandException)
                {
                    remoteFailed = true;
                }

                try
                {
                    IReadOnlyList<GitTag> remoteTags = await _gitService.Remotes.GetRemoteTagsAsync(
                        repository,
                        remote,
                        cancellationToken);
                    _remoteTagCache[remote.Name] = remoteTags;
                }
                catch (Exception exception) when (exception is GitRemoteOperationException or GitCommandException)
                {
                    remoteFailed = true;
                }

                _remoteTagLoadAttempts.Add(remote.Name);

                if (remoteFailed)
                {
                    failedRemotes.Add(remote.Name);
                }
            }

            await RefreshBranchesCoreAsync();
            if (failedRemotes.Count > 0)
            {
                ShowWarning(string.Format(
                    _localizationService.GetString("RemoteRefreshPartialFailure"),
                    string.Join(", ", failedRemotes.Distinct(StringComparer.Ordinal))));
            }
        }
        catch (OperationCanceledException)
        {
            ShowInfo(_localizationService.GetString("RemoteOperationCanceled"));
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitRemoteOperationException exception)
        {
            ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
    }

    private async Task RefreshBranchesCoreAsync(bool refreshLoadedTags = true)
    {
        ClearResultMessages();
        string? repositoryPath = _mainWindowViewModel.CurrentRepository?.Path;
        if (!string.Equals(_lastRepositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            ResetLazyDataState();
        }
        _hasRefreshed = true;

        if (_mainWindowViewModel.CurrentRepository is null)
        {
            SetBranchOperationState(GitOperationState.None);
            Branches.Clear();
            RemoteBranches.Clear();
            Tags.Clear();
            RemoteTags.Clear();
            FilteredBranches.Clear();
            FilteredTags.Clear();
            SelectedBranch = null;
            SelectedTag = null;
            HasNoBranches = false;
            HasNoTags = false;
            ShowError(_localizationService.GetString("OpenRepositoryBeforeBranches"));
            NotifyReferenceListChanged();
            return;
        }

        try
        {
            string? selectedRemoteName = string.IsNullOrWhiteSpace(_mainWindowViewModel.SelectedRemoteName)
                ? SelectedRemote?.Name
                : _mainWindowViewModel.SelectedRemoteName;
            Task<IReadOnlyList<GitRemote>> remotesTask = _gitService.GetRemotesAsync(_mainWindowViewModel.CurrentRepository);
            Task<GitOperationState> operationStateTask = _gitService.GetOperationStateAsync(_mainWindowViewModel.CurrentRepository);
            Task<IReadOnlyList<GitBranch>> branchesTask = _gitService.GetLocalBranchesAsync(_mainWindowViewModel.CurrentRepository);
            Task<IReadOnlyList<GitBranch>> remoteBranchesTask = _gitService.GetRemoteBranchesAsync(_mainWindowViewModel.CurrentRepository);
            Task<IReadOnlyDictionary<string, string>> branchDescriptionsTask =
                _gitService.Configuration.GetBranchDescriptionsAsync(_mainWindowViewModel.CurrentRepository);
            await Task.WhenAll(remotesTask, operationStateTask, branchesTask, remoteBranchesTask, branchDescriptionsTask);

            IReadOnlyList<GitRemote> remotes = await remotesTask;
            Remotes = remotes.ToList();
            GitRemote? resolvedRemote = Remotes.FirstOrDefault(item => item.Name == selectedRemoteName)
                ?? Remotes.FirstOrDefault(item => item.Name == "origin")
                ?? Remotes.FirstOrDefault();
            if (!Equals(SelectedRemote, resolvedRemote))
            {
                SelectedRemote = resolvedRemote;
            }

            var selectedBranchName = SelectedBranch?.Name;
            var selectedTagName = SelectedTag?.Name;
            GitOperationState operationState = await operationStateTask;
            IReadOnlyList<GitBranch> branches = await branchesTask;
            IReadOnlyDictionary<string, string> branchDescriptions = await branchDescriptionsTask;
            IReadOnlyList<GitBranch> allRemoteBranches = await remoteBranchesTask;
            IReadOnlyList<GitBranch> remoteBranches = allRemoteBranches;
            SynchronizationSnapshot = null;
            RemoteBranchSynchronizationSnapshot = null;
            _remoteBranchSynchronizationRemoteName = "";
            _lastRepositoryFetch = null;
            _isLastRepositoryFetchLoaded = false;
            OnPropertyChanged(nameof(LastRepositoryFetchText));
            _lastRepositoryPath = _mainWindowViewModel.CurrentRepository.Path;
            SetBranchOperationState(operationState);

            Branches.Clear();
            foreach (GitBranch branch in branches)
            {
                Branches.Add(branchDescriptions.TryGetValue(branch.Name, out string? description)
                    ? branch.WithConfigDescription(description)
                    : branch);
            }

            RemoteBranches.Clear();
            foreach (var branch in remoteBranches)
            {
                RemoteBranches.Add(branch);
            }

            HasNoBranches = Branches.Count == 0 && RemoteBranches.Count == 0;
            ApplyBranchFilter(selectedBranchName);
            if (_areLocalTagsLoaded && refreshLoadedTags)
            {
                Task<IReadOnlyList<GitTag>> tagsTask = _gitService.GetLocalTagsAsync(_mainWindowViewModel.CurrentRepository);
                Task<string?> headTask = _gitService.Tags.GetHeadCommitHashAsync(_mainWindowViewModel.CurrentRepository);
                await Task.WhenAll(tagsTask, headTask);
                _tagHeadCommitHash = await headTask;
                ReplaceLocalTags(await tagsTask, _tagHeadCommitHash, selectedTagName);
                ReplaceRemoteTags(GetCachedRemoteTags(), _tagHeadCommitHash, selectedTagName);
            }
            NotifySelectedDetailsChanged();
        }
        catch (FileNotFoundException)
        {
            ShowError(_localizationService.GetString("GitExecutableNotFound"));
        }
        catch (DirectoryNotFoundException)
        {
            ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
        }
        catch (GitRemoteOperationException exception)
        {
            ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
        }
        catch (GitCommandException exception)
        {
            ShowError(_localizationService.GetString("GitBranchCommandFailed"), exception.Message);
        }
        finally
        {
            NotifyReferenceListChanged();
        }
    }

    private async Task CheckoutSelectedBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null)
        {
            return;
        }

        var branchName = SelectedBranch.Name;
        await ExecuteBranchOperationAsync(string.Format(_localizationService.GetString("BranchCheckoutProgressMessage"), branchName),
            () => _gitService.Branches.CheckoutAsync(_mainWindowViewModel.CurrentRepository, SelectedBranch),
            async () =>
            {
                _mainWindowViewModel.UpdateCurrentBranch(branchName);
                _repositoryViewModel.UpdateCurrentBranch(branchName);
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("BranchCheckoutSucceeded"), branchName));
            });
    }

    private async Task CheckoutRemoteBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null || !SelectedBranch.IsRemote)
        {
            return;
        }

        var branch = SelectedBranch;
        var localBranchName = "";
        await ExecuteBranchOperationAsync(string.Format(_localizationService.GetString("BranchCheckoutProgressMessage"), branch.Name),
            async () => localBranchName = await _gitService.Branches.CheckoutRemoteAsync(_mainWindowViewModel.CurrentRepository, branch),
            async () =>
            {
                var checkedOutBranchName = string.IsNullOrWhiteSpace(localBranchName) ? branch.Name : localBranchName;
                _mainWindowViewModel.UpdateCurrentBranch(checkedOutBranchName);
                _repositoryViewModel.UpdateCurrentBranch(checkedOutBranchName);
                await RefreshBranchesCoreAsync();
                SelectedLocalBranch = Branches.FirstOrDefault(item => item.Name == checkedOutBranchName) ?? SelectedLocalBranch;
                ShowSuccess(string.Format(_localizationService.GetString("RemoteBranchCheckoutSucceeded"), branch.Name, checkedOutBranchName));
            });
    }

    private async Task FetchSelectedRemoteBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null ||
            SelectedBranch?.IsRemote != true ||
            !IsSelectedRemoteBranchMissingLocal)
        {
            return;
        }

        GitBranch remoteBranch = SelectedBranch;
        string localBranchName = SelectedRemoteBranchLocalName;
        await ExecuteBranchOperationAsync(
            string.Format(_localizationService.GetString("FetchBranchProgressMessage"), remoteBranch.Name),
            () => _gitService.Branches.CreateLocalFromRemoteAsync(_mainWindowViewModel.CurrentRepository, remoteBranch),
            async () =>
            {
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(
                    _localizationService.GetString("FetchBranchSucceeded"),
                    localBranchName,
                    remoteBranch.Name));
            });
    }

    private async Task CheckoutContextBranchAsync(object? parameter)
    {
        if (parameter is not GitBranch branch || branch.IsCurrent)
        {
            return;
        }

        SelectBranch(branch);
        if (branch.IsRemote)
        {
            await CheckoutRemoteBranchAsync();
        }
        else
        {
            await CheckoutSelectedBranchAsync();
        }
    }

    private async Task CreateBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeBranches"));
            return;
        }

        BranchCreationRequest? request = await _dialogService.ShowCreateBranchDialogAsync(
            _mainWindowViewModel.CurrentRepository);
        if (request is null)
        {
            return;
        }

        string progressMessageKey = request.Mode switch
        {
            BranchCreationMode.CheckoutEmptyOrphan => "OrphanBranchCheckoutProgressMessage",
            BranchCreationMode.EmptyOrphanWithInitialCommit => "OrphanBranchCreateProgressMessage",
            BranchCreationMode.CheckoutOrphanFromCommit => "OrphanBranchSnapshotCheckoutProgressMessage",
            BranchCreationMode.OrphanFromCommit => "OrphanBranchSnapshotCreateProgressMessage",
            BranchCreationMode.CheckoutFromCommit => "BranchCreateAndCheckoutProgressMessage",
            _ => "BranchCreateProgressMessage"
        };
        Func<Task> createOperation = request.Mode switch
        {
            BranchCreationMode.CheckoutEmptyOrphan => () => _gitService.Branches.CreateAndCheckoutOrphanBranchAsync(
                _mainWindowViewModel.CurrentRepository,
                request.BranchName),
            BranchCreationMode.EmptyOrphanWithInitialCommit => () => _gitService.Branches.CreateOrphanBranchAsync(
                _mainWindowViewModel.CurrentRepository,
                request.BranchName,
                _localizationService.GetString("OrphanBranchInitialCommitMessage")),
            BranchCreationMode.CheckoutOrphanFromCommit => () => _gitService.Branches.CreateOrphanBranchFromCommitAsync(
                _mainWindowViewModel.CurrentRepository,
                request.BranchName,
                request.StartPointHash!,
                _localizationService.GetString("OrphanBranchSnapshotCommitMessage"),
                checkout: true),
            BranchCreationMode.OrphanFromCommit => () => _gitService.Branches.CreateOrphanBranchFromCommitAsync(
                _mainWindowViewModel.CurrentRepository,
                request.BranchName,
                request.StartPointHash!,
                _localizationService.GetString("OrphanBranchSnapshotCommitMessage"),
                checkout: false),
            BranchCreationMode.CheckoutFromCommit => () => _gitService.Branches.CreateAndCheckoutBranchAsync(
                _mainWindowViewModel.CurrentRepository,
                request.BranchName,
                request.StartPointHash!),
            _ => () => _gitService.Branches.CreateBranchAsync(
                _mainWindowViewModel.CurrentRepository,
                request.BranchName,
                request.StartPointHash!)
        };

        await ExecuteBranchOperationAsync(
            string.Format(_localizationService.GetString(progressMessageKey), request.BranchName),
            createOperation,
            async () =>
            {
                bool checksOutBranch = request.Mode is
                    BranchCreationMode.CheckoutEmptyOrphan or
                    BranchCreationMode.CheckoutOrphanFromCommit or
                    BranchCreationMode.CheckoutFromCommit;
                if (checksOutBranch)
                {
                    _mainWindowViewModel.UpdateCurrentBranch(request.BranchName);
                    _repositoryViewModel.UpdateCurrentBranch(request.BranchName);
                }

                await RefreshBranchesCoreAsync();
                if (request.Mode == BranchCreationMode.CheckoutEmptyOrphan)
                {
                    SelectedBranch = null;
                    ShowSuccess(string.Format(
                        _localizationService.GetString("OrphanBranchCheckoutSucceeded"),
                        request.BranchName));
                }
                else
                {
                    SelectedLocalBranch = Branches.FirstOrDefault(branch => branch.Name == request.BranchName)
                        ?? SelectedLocalBranch;
                    string successMessageKey = request.Mode switch
                    {
                        BranchCreationMode.EmptyOrphanWithInitialCommit => "OrphanBranchCreateSucceeded",
                        BranchCreationMode.CheckoutOrphanFromCommit => "OrphanBranchSnapshotCheckoutSucceeded",
                        BranchCreationMode.OrphanFromCommit => "OrphanBranchSnapshotCreateSucceeded",
                        BranchCreationMode.CheckoutFromCommit => "BranchCreateAndCheckoutSucceeded",
                        _ => "BranchCreateSucceeded"
                    };
                    ShowSuccess(string.Format(
                        _localizationService.GetString(successMessageKey),
                        request.BranchName));
                }
            });
    }

    private async Task RenameSelectedBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null || SelectedBranch.IsRemote)
        {
            return;
        }

        var branch = SelectedBranch;
        var newBranchName = await _dialogService.ShowRenameBranchDialogAsync(branch);
        if (newBranchName is null)
        {
            return;
        }

        await ExecuteBranchOperationAsync(string.Format(_localizationService.GetString("BranchRenameProgressMessage"), branch.Name, newBranchName),
            () => _gitService.Branches.RenameBranchAsync(_mainWindowViewModel.CurrentRepository, branch, newBranchName),
            async () =>
            {
                if (branch.IsCurrent)
                {
                    _mainWindowViewModel.UpdateCurrentBranch(newBranchName);
                    _repositoryViewModel.UpdateCurrentBranch(newBranchName);
                }

                await RefreshBranchesCoreAsync();
                SelectedLocalBranch = Branches.FirstOrDefault(item => item.Name == newBranchName) ?? SelectedLocalBranch;
                ShowSuccess(string.Format(_localizationService.GetString("BranchRenameSucceeded"), branch.Name, newBranchName));
            });
    }

    private async Task RenameContextBranchAsync(object? parameter)
    {
        if (parameter is not GitBranch branch || !branch.IsLocal)
        {
            return;
        }

        SelectBranch(branch);
        await RenameSelectedBranchAsync();
    }

    private Task EditSelectedBranchDescriptionAsync()
    {
        return EditBranchDescriptionAsync(SelectedBranch);
    }

    private async Task EditContextBranchDescriptionAsync(object? parameter)
    {
        if (parameter is not GitBranch branch || !branch.IsLocal)
        {
            return;
        }

        SelectBranch(branch);
        await EditBranchDescriptionAsync(branch);
    }

    private async Task EditBranchDescriptionAsync(GitBranch? branch)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null || branch?.IsLocal != true)
        {
            return;
        }

        string? description = await _dialogService.ShowBranchDescriptionDialogAsync(branch);
        if (description is null || description.Equals(branch.ConfigDescription, StringComparison.Ordinal))
        {
            return;
        }

        await ExecuteBranchOperationAsync(
            string.Format(_localizationService.GetString("BranchDescriptionProgressMessage"), branch.Name),
            () => _gitService.Configuration.SetBranchDescriptionAsync(repository, branch.Name, description),
            () =>
            {
                UpdateBranchDescription(branch.Name, description);
                ShowSuccess(_localizationService.GetString("BranchDescriptionSaved"));
                return Task.CompletedTask;
            });
    }

    private Task DeleteSelectedBranchDescriptionAsync()
    {
        return DeleteBranchDescriptionAsync(SelectedBranch);
    }

    private async Task DeleteContextBranchDescriptionAsync(object? parameter)
    {
        if (parameter is not GitBranch branch || !branch.CanEditConfigDescription)
        {
            return;
        }

        SelectBranch(branch);
        await DeleteBranchDescriptionAsync(branch);
    }

    private async Task DeleteBranchDescriptionAsync(GitBranch? branch)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        if (repository is null || branch?.CanEditConfigDescription != true)
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("BranchDescriptionDeleteDialogTitle"),
            string.Format(_localizationService.GetString("BranchDescriptionDeleteDialogMessage"), branch.Name),
            _localizationService.GetString("BranchDescriptionDeleteDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        await ExecuteBranchOperationAsync(
            string.Format(_localizationService.GetString("BranchDescriptionDeleteProgressMessage"), branch.Name),
            () => _gitService.Configuration.SetBranchDescriptionAsync(repository, branch.Name, ""),
            () =>
            {
                UpdateBranchDescription(branch.Name, "");
                ShowSuccess(_localizationService.GetString("BranchDescriptionDeleted"));
                return Task.CompletedTask;
            });
    }

    private async Task DeleteSelectedBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null)
        {
            return;
        }

        RepositoryInfo repository = _mainWindowViewModel.CurrentRepository;
        var branch = SelectedBranch;
        bool confirmed = await _dialogService.ConfirmAsync(_localizationService.GetString("BranchDeleteTitle")
                                                      , string.Format(_localizationService.GetString("BranchDeleteMessage"), branch.Name)
                                                                    , _localizationService.GetString("BranchDeletePrimaryButtonText"));
        if (!confirmed)
        {
            return;
        }

        if (branch.IsRemote)
        {
            int slashIndex = branch.Name.IndexOf('/');
            if (slashIndex <= 0)
            {
                ShowError(string.Format(_localizationService.GetString("BranchDeleteFailed"), branch.Name));
                return;
            }

            string remoteName = branch.Name[..slashIndex];
            string remoteBranchName = branch.Name[(slashIndex + 1)..];

            IReadOnlyList<GitRemote> remotes = await _gitService.GetRemotesAsync(repository);
            var remote = remotes.FirstOrDefault(r => r.Name == remoteName);
            if (remote is null)
            {
                ShowError(string.Format(_localizationService.GetString("RemoteNotFound"), remoteName));
                return;
            }

            await RunRemoteOperationAsync(
                string.Format(_localizationService.GetString("BranchDeleteProgressMessage"), branch.Name),
                async cancellationToken =>
                {
                    await _gitService.Remotes.DeleteBranchAsync(repository, remote, remoteBranchName, cancellationToken);
                    await RefreshBranchesCoreAsync();
                    ShowSuccess(string.Format(_localizationService.GetString("BranchDeleteSucceeded"), branch.Name));
                });

            return;
        }

        BranchDeleteResult deleteResult = await DeleteLocalBranchAsync(repository, branch, force: false);
        if (deleteResult != BranchDeleteResult.NeedsForce)
        {
            return;
        }

        bool forceConfirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("BranchForceDeleteTitle"),
            string.Format(_localizationService.GetString("BranchForceDeleteMessage"), branch.Name),
            _localizationService.GetString("BranchForceDeletePrimaryButtonText"));

        if (!forceConfirmed)
        {
            return;
        }

        await DeleteLocalBranchAsync(repository, branch, force: true);
    }

    private async Task DeleteContextBranchAsync(object? parameter)
    {
        if (parameter is not GitBranch branch || branch.IsCurrent)
        {
            return;
        }

        SelectBranch(branch);
        await DeleteSelectedBranchAsync();
    }

    private async Task CreateTagAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeBranches"));
            return;
        }

        var commits = await LoadCommitsForCreateDialogAsync();
        if (commits is null)
        {
            return;
        }

        if (commits.Count == 0)
        {
            ShowError(_localizationService.GetString("NoCommitsForTagCreation"));
            return;
        }

        var request = await _dialogService.ShowCreateTagDialogAsync(commits);
        if (request is null)
        {
            return;
        }

        await ExecuteTagOperationAsync(
            string.Format(_localizationService.GetString("TagCreateProgressMessage"), request.TagName),
            () => _gitService.Tags.CreateTagAsync(_mainWindowViewModel.CurrentRepository, request),
            async () =>
            {
                await RefreshBranchesCoreAsync();
                SelectedTag = Tags.FirstOrDefault(tag => tag.Name == request.TagName) ?? SelectedTag;
                ShowSuccess(string.Format(_localizationService.GetString("TagCreateSucceeded"), request.TagName));
            });
    }

    private async Task DeleteSelectedTagAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedTag is null)
        {
            return;
        }

        var tag = SelectedTag;
        string confirmationMessageKey = tag.IsRemote ? "RemoteTagDeleteMessage" : "TagDeleteMessage";
        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("TagDeleteTitle"),
            string.Format(_localizationService.GetString(confirmationMessageKey), tag.Name),
            _localizationService.GetString("TagDeletePrimaryButtonText"));

        if (!confirmed)
        {
            return;
        }

        if (tag.IsRemote)
        {
            IReadOnlyList<GitRemote> remotes = await _gitService.GetRemotesAsync(_mainWindowViewModel.CurrentRepository);
            var remote = remotes.FirstOrDefault(item => item.Name == tag.RemoteName);
            if (remote is null)
            {
                ShowError(string.Format(_localizationService.GetString("RemoteNotFound"), tag.RemoteName));
                return;
            }

            await RunRemoteOperationAsync(
                string.Format(_localizationService.GetString("TagDeleteProgressMessage"), tag.Name),
                async cancellationToken =>
                {
                    await _gitService.Remotes.DeleteTagAsync(
                        _mainWindowViewModel.CurrentRepository,
                        remote,
                        tag.RemoteTagName,
                        cancellationToken);
                    if (_remoteTagCache.TryGetValue(remote.Name, out IReadOnlyList<GitTag>? cachedTags))
                    {
                        _remoteTagCache[remote.Name] = cachedTags
                            .Where(item => !item.RemoteTagName.Equals(tag.RemoteTagName, StringComparison.Ordinal))
                            .ToList();
                    }
                    await RefreshBranchesCoreAsync();
                    ShowSuccess(string.Format(_localizationService.GetString("RemoteTagDeleteSucceeded"), tag.Name));
                });

            return;
        }

        await ExecuteTagOperationAsync(
            string.Format(_localizationService.GetString("TagDeleteProgressMessage"), tag.Name),
            () => _gitService.Tags.DeleteTagAsync(_mainWindowViewModel.CurrentRepository, tag),
            async () =>
            {
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("TagDeleteSucceeded"), tag.Name));
            });
    }

    private async Task CheckoutSelectedTagAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedTag is null)
        {
            return;
        }

        GitTag tag = SelectedTag;
        bool confirmed = await _dialogService.ConfirmAsync(
            _localizationService.GetString("TagCheckoutDetachedDialogTitle"),
            string.Format(_localizationService.GetString("TagCheckoutDetachedDialogMessage"), tag.Name),
            _localizationService.GetString("TagCheckoutDetachedDialogPrimaryButton"));
        if (!confirmed)
        {
            return;
        }

        if (tag.IsRemote && !await PrepareRemoteTagForCheckoutAsync(tag))
        {
            return;
        }

        GitTag checkoutTag = FindLocalTag(tag) ?? tag;
        await ExecuteTagOperationAsync(
            string.Format(_localizationService.GetString("TagCheckoutProgressMessage"), tag.Name),
            () => _gitService.Tags.CheckoutTagAsync(_mainWindowViewModel.CurrentRepository, checkoutTag),
            async () =>
            {
                string detachedHead = $"Detached at {tag.ShortCommitHash[..Math.Min(7, tag.ShortCommitHash.Length)]}";
                _mainWindowViewModel.UpdateCurrentBranch(detachedHead);
                _repositoryViewModel.UpdateCurrentBranch(detachedHead);
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("TagCheckoutSucceeded"), tag.Name));
            });
    }

    private async Task<bool> PrepareRemoteTagForCheckoutAsync(GitTag remoteTag)
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            return false;
        }

        GitTag? localTag = FindLocalTag(remoteTag);
        if (localTag is null)
        {
            return await FetchRemoteTagCoreAsync(remoteTag, force: false);
        }

        if (localTag.ReferenceObjectHash.Equals(remoteTag.ReferenceObjectHash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        TagConflictResolution resolution = await _dialogService.ShowTagConflictDialogAsync(
            remoteTag.Name,
            localTag.ReferenceObjectHash,
            remoteTag.ReferenceObjectHash,
            remoteTag.RemoteName);
        if (resolution == TagConflictResolution.OpenRemoteTemporarily)
        {
            await ExecuteTagOperationAsync(
                string.Format(_localizationService.GetString("TagCheckoutProgressMessage"), remoteTag.Name),
                () => _gitService.Tags.CheckoutTagAsync(_mainWindowViewModel.CurrentRepository, remoteTag),
                async () =>
                {
                    string detachedHead = $"Detached at {remoteTag.ShortCommitHash[..Math.Min(7, remoteTag.ShortCommitHash.Length)]}";
                    _mainWindowViewModel.UpdateCurrentBranch(detachedHead);
                    _repositoryViewModel.UpdateCurrentBranch(detachedHead);
                    await RefreshBranchesCoreAsync();
                    ShowSuccess(string.Format(_localizationService.GetString("TagCheckoutSucceeded"), remoteTag.Name));
                });
            return false;
        }

        return resolution == TagConflictResolution.ReplaceLocal
            && await FetchRemoteTagCoreAsync(remoteTag, force: true);
    }

    private async Task FetchSelectedRemoteTagAsync()
    {
        if (SelectedTag?.IsRemote != true || _mainWindowViewModel.CurrentRepository is null)
        {
            return;
        }

        GitTag remoteTag = SelectedTag;
        GitRemote? remote = FindRemote(remoteTag.RemoteName);
        if (remote is null)
        {
            ShowError(string.Format(
                _localizationService.GetString("RemoteNotFound"),
                remoteTag.RemoteName));
            return;
        }

        GitTag? localTag = FindLocalTag(remoteTag);
        bool force = false;
        if (localTag is not null && !localTag.ReferenceObjectHash.Equals(remoteTag.ReferenceObjectHash, StringComparison.OrdinalIgnoreCase))
        {
            bool replace = await _dialogService.ConfirmAsync(
                _localizationService.GetString("RemoteTagConflictDialogTitle"),
                string.Format(
                    _localizationService.GetString("RemoteTagFetchConflictMessage"),
                    remoteTag.Name,
                    localTag.ReferenceObjectHash,
                    remote.Name,
                    remoteTag.ReferenceObjectHash),
                _localizationService.GetString("RemoteTagConflictReplaceButton"));
            if (!replace) return;
            force = true;
        }

        if (localTag is not null && !force)
        {
            ShowInfo(_localizationService.GetString("RemoteTagAlreadyLocal"));
            return;
        }

        await FetchRemoteTagCoreAsync(remoteTag, force);
    }

    private async Task<bool> FetchRemoteTagCoreAsync(GitTag remoteTag, bool force)
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            return false;
        }

        GitRemote? remote = FindRemote(remoteTag.RemoteName);
        if (remote is null)
        {
            ShowError(string.Format(
                _localizationService.GetString("RemoteNotFound"),
                remoteTag.RemoteName));
            return false;
        }

        bool succeeded = false;
        await RunRemoteOperationAsync(
            string.Format(
                _localizationService.GetString("FetchTagProgressMessage"),
                remoteTag.Name,
                remote.Name),
            async cancellationToken =>
            {
                await _gitService.Remotes.FetchTagAsync(
                    _mainWindowViewModel.CurrentRepository,
                    remote,
                    remoteTag.RemoteTagName,
                    force,
                    cancellationToken);
                await RefreshBranchesCoreAsync();
                succeeded = true;
                ShowSuccess(string.Format(_localizationService.GetString("FetchTagSucceeded"), remoteTag.Name));
            });
        return succeeded;
    }

    private async Task CheckoutContextTagAsync(object? parameter)
    {
        if (parameter is not GitTag tag)
        {
            return;
        }

        SelectedTag = tag;
        await CheckoutSelectedTagAsync();
    }

    private async Task DeleteContextTagAsync(object? parameter)
    {
        if (parameter is not GitTag tag)
        {
            return;
        }

        SelectedTag = tag;
        await DeleteSelectedTagAsync();
    }

    private async Task CreateBranchFromSelectedTagAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedTag is null)
        {
            return;
        }

        var tag = SelectedTag;
        if (tag.IsRemote)
        {
            await FetchSelectedRemoteTagAsync();
            GitTag? localTag = Tags.FirstOrDefault(item => item.Name == tag.RemoteTagName
                && item.ReferenceObjectHash.Equals(tag.ReferenceObjectHash, StringComparison.OrdinalIgnoreCase));
            if (localTag is null)
            {
                return;
            }

            tag = localTag;
        }
        string? branchName = await _dialogService.ShowTextInputAsync(new TextInputDialogRequest(
            _localizationService.GetString("CreateBranchFromTagDialogTitle"),
            _localizationService.GetString("CreateBranchFromTagDialogTextBoxHeader"),
            "",
            _localizationService.GetString("CreateBranchFromTagDialogPrimaryButton"),
            _localizationService.GetString("CreateBranchFromTagDialogCloseButton"),
            _localizationService.GetString("CreateBranchFromTagDialogPlaceholder")));

        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        string newBranchName = branchName.Trim();
        string startPoint = tag.IsRemote ? tag.ObjectHash : tag.Name;
        await ExecuteBranchOperationAsync(
            string.Format(_localizationService.GetString("BranchCreateProgressMessage"), newBranchName),
            () => _gitService.Branches.CreateBranchAsync(_mainWindowViewModel.CurrentRepository, newBranchName, startPoint),
            async () =>
            {
                await RefreshBranchesCoreAsync();
                ReferenceKind = ReferenceListKind.Branches;
                BranchScope = BranchListScope.Local;
                SelectedBranch = Branches.FirstOrDefault(branch => branch.Name == newBranchName) ?? SelectedBranch;
                ShowSuccess(string.Format(_localizationService.GetString("BranchCreateFromTagSucceeded"), newBranchName, tag.Name));
            });
    }

    private async Task CreateBranchFromContextTagAsync(object? parameter)
    {
        if (parameter is not GitTag tag)
        {
            return;
        }

        SelectedTag = tag;
        await CreateBranchFromSelectedTagAsync();
    }

    private async Task<BranchDeleteResult> DeleteLocalBranchAsync(RepositoryInfo repository, GitBranch branch, bool force)
    {
        BranchDeleteResult result = BranchDeleteResult.Failed;
        await RunGitOperationAsync(string.Format(_localizationService.GetString("BranchDeleteProgressMessage"), branch.Name), async () =>
        {
            try
            {
                ClearResultMessages();
                if (force)
                {
                    await _gitService.Branches.ForceDeleteBranchAsync(repository, branch);
                }
                else
                {
                    await _gitService.Branches.DeleteBranchAsync(repository, branch);
                }

                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("BranchDeleteSucceeded"), branch.Name));
                result = BranchDeleteResult.Deleted;
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception) when (!force && IsBranchNotFullyMerged(exception))
            {
                ClearResultMessages();
                result = BranchDeleteResult.NeedsForce;
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitBranchCommandFailed"), exception.Message);
            }
        });

        return result;
    }

    private static bool IsBranchNotFullyMerged(GitCommandException exception)
    {
        return exception.Message.Contains("not fully merged", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("git branch -D", StringComparison.OrdinalIgnoreCase);
    }

    private async Task MergeSelectedBranchAsync(object? parameter)
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null)
            return;

        bool withoutCommit = parameter is string p && bool.TryParse(p, out bool val) && val == true;

        var branch = SelectedBranch;
        var confirmed = await ConfirmBranchOperationAsync(
            "MergeBranchDialogTitle",
            withoutCommit ? "MergeBranchWithoutCommitDialogMessage" : "MergeBranchDialogMessage",
            "MergeBranchDialogPrimaryButton",
            branch);

        if (!confirmed)
            return;


        BranchMergeResult mergeResult = BranchMergeResult.Canceled;
        GitBranchMergeOptions options = new(NoCommit: withoutCommit);
        await ExecuteBranchOperationAsync(string.Format(_localizationService.GetString("MergeBranchProgressMessage"), branch.Name),
            async () =>
            {
                mergeResult = await MergeWithUnrelatedHistoriesConfirmationAsync(
                    _mainWindowViewModel.CurrentRepository,
                    branch,
                    options);
                return mergeResult != BranchMergeResult.Canceled;
            },
            async () =>
            {
                await RefreshBranchesCoreAsync();
                string successMessageKey = mergeResult == BranchMergeResult.CompletedWithoutCommit
                    ? "MergeBranchWithoutCommitSucceeded"
                    : "MergeBranchSucceeded";
                ShowSuccess(string.Format(
                    _localizationService.GetString(successMessageKey),
                    branch.Name));
            });
    }

    private async Task SquashMergeSelectedBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null)
        {
            return;
        }

        var branch = SelectedBranch;
        var confirmed = await ConfirmBranchOperationAsync(
            "SquashMergeBranchDialogTitle",
            "SquashMergeBranchDialogMessage",
            "SquashMergeBranchDialogPrimaryButton",
            branch);

        if (!confirmed)
        {
            return;
        }

        GitBranchMergeOptions options = new(Squash: true);
        await ExecuteBranchOperationAsync(string.Format(_localizationService.GetString("MergeBranchProgressMessage"), branch.Name),
            async () => await MergeWithUnrelatedHistoriesConfirmationAsync(
                _mainWindowViewModel.CurrentRepository,
                branch,
                options) != BranchMergeResult.Canceled,
            async () =>
            {
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("SquashMergeBranchSucceeded"), branch.Name));
            });
    }

    private async Task PrepareSelectedBranchSnapshotAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitBranch? sourceBranch = SelectedBranch;
        if (repository is null || sourceBranch is null || sourceBranch.IsCurrent)
        {
            return;
        }

        string currentBranchName = _mainWindowViewModel.CurrentBranch;
        string successMessage = string.Format(
            _localizationService.GetString("PrepareBranchSnapshotSucceeded"),
            sourceBranch.Name);

        await ExecuteBranchOperationAsync(
            string.Format(
                _localizationService.GetString("PrepareBranchSnapshotProgressMessage"),
                sourceBranch.Name),
            async () =>
            {
                GitOperationState operationState = await _gitService.GetOperationStateAsync(repository);
                if (operationState.Kind != GitOperationKind.None)
                {
                    ShowError(_localizationService.GetString("PrepareBranchSnapshotOperationInProgress"));
                    return false;
                }

                GitStatusSnapshot status = await _gitService.GetStatusAsync(repository);
                if (!IsCleanWorkingTree(status))
                {
                    ShowError(_localizationService.GetString("PrepareBranchSnapshotWorkingTreeNotClean"));
                    return false;
                }

                bool confirmed = await _dialogService.ConfirmAsync(
                    string.Format(
                        _localizationService.GetString("PrepareBranchSnapshotDialogTitle"),
                        sourceBranch.Name),
                    string.Format(
                        _localizationService.GetString("PrepareBranchSnapshotDialogMessage"),
                        currentBranchName,
                        sourceBranch.Name),
                    _localizationService.GetString("PrepareBranchSnapshotDialogPrimaryButton"));
                if (!confirmed)
                {
                    return false;
                }

                await _gitService.Branches.PrepareSnapshotAsync(repository, sourceBranch);
                return true;
            },
            () =>
            {
                _mainWindowViewModel.RequestChangesNavigation(successMessage);
                return Task.CompletedTask;
            });
    }

    private static bool IsCleanWorkingTree(GitStatusSnapshot status)
    {
        return status.StagedChanges.Count == 0
            && status.UnstagedChanges.Count == 0
            && status.ConflictedChanges.Count == 0;
    }

    private async Task<BranchMergeResult> MergeWithUnrelatedHistoriesConfirmationAsync(
        RepositoryInfo repository,
        GitBranch branch,
        GitBranchMergeOptions options)
    {
        try
        {
            await _gitService.Branches.MergeAsync(repository, branch, options);
            return options.NoCommit || options.Squash
                ? BranchMergeResult.CompletedWithoutCommit
                : BranchMergeResult.Completed;
        }
        catch (GitCommandException exception) when (GitMergeFailureDetector.IsUnrelatedHistories(exception))
        {
            bool confirmed = await ConfirmBranchOperationAsync(
                "UnrelatedHistoriesMergeDialogTitle",
                "UnrelatedHistoriesMergeDialogMessage",
                "UnrelatedHistoriesMergeDialogPrimaryButton",
                branch);
            if (!confirmed)
            {
                return BranchMergeResult.Canceled;
            }

            await _gitService.Branches.MergeAsync(
                repository,
                branch,
                options with { AllowUnrelatedHistories = true });
            return BranchMergeResult.CompletedWithoutCommit;
        }
    }

    private async Task RebaseSelectedBranchAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null || SelectedBranch is null)
        {
            return;
        }

        var branch = SelectedBranch;
        var confirmed = await ConfirmBranchOperationAsync(
            "RebaseBranchDialogTitle",
            "RebaseBranchDialogMessage",
            "RebaseBranchDialogPrimaryButton",
            branch);

        if (!confirmed)
        {
            return;
        }

        GitBranchRebaseResult? rebaseResult = null;
        await ExecuteBranchOperationAsync(string.Format(_localizationService.GetString("RebaseBranchProgressMessage"), branch.Name),
            async () => rebaseResult = await _gitService.Branches.RebaseAsync(
                _mainWindowViewModel.CurrentRepository,
                branch),
            async () =>
            {
                await RefreshBranchesCoreAsync();
                string messageKey = rebaseResult?.HeadChanged == true
                    ? "RebaseBranchSucceeded"
                    : "RebaseBranchAlreadyUpToDate";
                ShowSuccess(string.Format(
                    _localizationService.GetString(messageKey),
                    branch.Name));
            });
    }

    private async Task ExecuteBranchOperationAsync(string progressMessage, System.Func<Task> operation, System.Func<Task> onSuccess)
    {
        await ExecuteBranchOperationAsync(
            progressMessage,
            async () =>
            {
                await operation();
                return true;
            },
            onSuccess);
    }

    private async Task ExecuteBranchOperationAsync(
        string progressMessage,
        System.Func<Task<bool>> operation,
        System.Func<Task> onSuccess)
    {
        await RunGitOperationAsync(progressMessage, async () =>
        {
            bool gitCommandFailed = false;
            try
            {
                ClearResultMessages();
                if (await operation())
                {
                    await onSuccess();
                }
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                gitCommandFailed = true;
                ShowError(_localizationService.GetString("GitBranchCommandFailed"), exception.Message);
            }
            finally
            {
                await RefreshBranchOperationStateAsync();
            }

            if (gitCommandFailed && (IsMergeInProgress || IsRebaseInProgress))
            {
                _mainWindowViewModel.RequestChangesNavigation(
                    _localizationService.GetString("ConflictResolutionRequiredOnChangesPage"));
            }
        });
    }

    private async Task RefreshBranchOperationStateAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            SetBranchOperationState(GitOperationState.None);
            return;
        }

        try
        {
            GitOperationState state = await _gitService.GetOperationStateAsync(
                _mainWindowViewModel.CurrentRepository);
            SetBranchOperationState(state);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (GitCommandException)
        {
        }
    }

    private void SetBranchOperationState(GitOperationState state)
    {
        IsMergeInProgress = state.Kind == GitOperationKind.Merge;
        IsRebaseInProgress = state.Kind == GitOperationKind.Rebase;
        HasOperationInProgress = state.Kind != GitOperationKind.None;
    }

    private async Task ExecuteTagOperationAsync(string progressMessage, System.Func<Task> operation, System.Func<Task> onSuccess)
    {
        await RunGitOperationAsync(progressMessage, async () =>
        {
            try
            {
                ClearResultMessages();
                await operation();
                await onSuccess();
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitRemoteOperationException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitTagCommandFailed"), exception.Message);
            }
        });
    }

    private Task<bool> ConfirmBranchOperationAsync(
        string titleKey,
        string messageKey,
        string primaryButtonKey,
        GitBranch branch)
    {
        return _dialogService.ConfirmAsync(
            _localizationService.GetString(titleKey),
            string.Format(_localizationService.GetString(messageKey), branch.Name),
            _localizationService.GetString(primaryButtonKey));
    }

    private async Task RunGitOperationAsync(string progressMessage, System.Func<Task> operation)
    {
        await _gitService.ExecuteAsync(async () =>
        {
            IsGitOperationRunning = true;
            ProgressMessage = progressMessage;
            try
            {
                await operation();
            }
            finally
            {
                IsGitOperationRunning = false;
            }
        });
    }

    private async Task RunQueuedReadOperationAsync(
        string progressMessage,
        System.Func<Task> operation)
    {
        _queuedReadOperationCount++;
        ProgressMessage = progressMessage;
        OnPropertyChanged(nameof(IsOperationProgressRunning));
        OnPropertyChanged(nameof(OperationProgressVisibility));
        OnPropertyChanged(nameof(CanUseRemoteSelector));
        PublishBranchesOperationState();
        try
        {
            await _gitService.ExecuteAsync(async () =>
            {
                ProgressMessage = progressMessage;
                await operation();
            });
        }
        finally
        {
            _queuedReadOperationCount--;
            if (_queuedReadOperationCount == 0 && !IsGitOperationRunning)
            {
                ProgressMessage = "";
            }

            OnPropertyChanged(nameof(IsOperationProgressRunning));
            OnPropertyChanged(nameof(OperationProgressVisibility));
            OnPropertyChanged(nameof(CanUseRemoteSelector));
            PublishBranchesOperationState();
        }
    }

    private void ClearResultMessages()
    {
        ClearNotification();
    }

    private void ShowError(string message)
    {
        ShowNotification(AppNotificationSeverity.Error, message);
    }

    private void ShowError(string message, string? details)
    {
        ShowNotification(AppNotificationSeverity.Error, message, details);
    }

    private void ShowSuccess(string message)
    {
        ShowNotification(AppNotificationSeverity.Success, message);
    }

    private void ShowInfo(string message)
    {
        ShowNotification(AppNotificationSeverity.Informational, message);
    }

    private void ShowWarning(string message)
    {
        ShowNotification(AppNotificationSeverity.Warning, message);
    }

    private Task CreateSelectedReferenceKindAsync()
    {
        return ReferenceKind == ReferenceListKind.Tags ? CreateTagAsync() : CreateBranchAsync();
    }

    private async Task ExecuteBranchPrimaryActionAsync()
    {
        await EnsureSelectedBranchWorktreesLoadedAsync();
        if (IsSelectedBranchInOtherWorktree)
        {
            await OpenSelectedBranchWorktreeAsync();
            return;
        }

        if (SelectedBranch?.IsRemote == true) await CheckoutRemoteBranchAsync();
        else await CheckoutSelectedBranchAsync();
    }

    private async Task OpenSelectedBranchWorktreeAsync()
    {
        GitWorktree? worktree = SelectedBranchWorktrees?.FirstOrDefault(item => !item.IsPrunable && !item.IsBare);
        if (worktree is null)
        {
            return;
        }

        if (await _repositoryViewModel.OpenRepositoryPathAsync(worktree.Path))
        {
            await RefreshBranchesCoreAsync();
            ShowSuccess(string.Format(_localizationService.GetString("WorktreeOpened"), worktree.Path));
        }
    }

    private async Task CreateSelectedBranchWorktreeAsync()
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitBranch? branch = SelectedBranch;
        if (repository is null || branch is null)
        {
            return;
        }

        string localBranchName = GetLocalBranchName(branch);
        GitBranch? localBranch = branch.IsRemote
            ? Branches.FirstOrDefault(item => item.Name.Equals(localBranchName, StringComparison.Ordinal))
            : branch;
        bool canUseExistingBranch = localBranch is not null;
        string defaultPath = CreateDefaultWorktreePath(repository, localBranchName);
        string newBranchName = branch.IsRemote && localBranch is null ? localBranchName : "";
        WorktreeCreationRequest? request = await _dialogService.ShowCreateWorktreeDialogAsync(
            repository,
            defaultPath,
            localBranch?.Name ?? branch.Name,
            newBranchName,
            branch.IsRemote && localBranch is null
                ? WorktreeCreationMode.NewBranch
                : WorktreeCreationMode.ExistingBranch,
            canUseExistingBranch);
        if (request is null)
        {
            return;
        }

        await ExecuteBranchOperationAsync(
            _localizationService.GetString("CreatingWorktreeProgress"),
            () => _gitService.Worktrees.AddAsync(repository, request),
            async () =>
            {
                await _repositoryViewModel.OpenRepositoryPathAsync(request.Path);
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("WorktreeCreated"), request.Path));
            });
    }

    private async Task OpenSelectedTagWorktreeAsync()
    {
        GitWorktree? worktree = SelectedTagWorktrees?.FirstOrDefault(item => !item.IsPrunable && !item.IsBare);
        if (worktree is null)
        {
            return;
        }

        if (await _repositoryViewModel.OpenRepositoryPathAsync(worktree.Path))
        {
            await RefreshBranchesCoreAsync();
            ShowSuccess(string.Format(_localizationService.GetString("WorktreeOpened"), worktree.Path));
        }
    }

    private async Task CreateSelectedTagWorktreeAsync()
    {
        await CreateSelectedTagWorktreeAsync(isDetached: true);
    }

    private async Task CreateSelectedTagBranchWorktreeAsync()
    {
        await CreateSelectedTagWorktreeAsync(isDetached: false);
    }

    private async Task CreateSelectedTagWorktreeAsync(bool isDetached)
    {
        RepositoryInfo? repository = _mainWindowViewModel.CurrentRepository;
        GitTag? tag = SelectedTag;
        if (repository is null || tag is null)
        {
            return;
        }

        string defaultPath = CreateDefaultWorktreePath(repository, tag.Name);
        string startPoint = tag.IsRemote ? tag.ObjectHash : tag.Name;
        WorktreeCreationRequest? request = await _dialogService.ShowCreateWorktreeDialogAsync(
            repository,
            defaultPath,
            startPoint,
            isDetached ? "" : tag.Name,
            isDetached ? WorktreeCreationMode.Detached : WorktreeCreationMode.NewBranch,
            canUseExistingBranch: false,
            startPointKind: tag.IsRemote ? GitRevisionKind.Commit : GitRevisionKind.Tag);
        if (request is null)
        {
            return;
        }

        await ExecuteBranchOperationAsync(
            _localizationService.GetString("CreatingWorktreeProgress"),
            () => _gitService.Worktrees.AddAsync(repository, request),
            async () =>
            {
                await _repositoryViewModel.OpenRepositoryPathAsync(request.Path);
                await RefreshBranchesCoreAsync();
                ShowSuccess(string.Format(_localizationService.GetString("WorktreeCreated"), request.Path));
            });
    }

    private static string CreateDefaultWorktreePath(RepositoryInfo repository, string name)
    {
        string safeName = string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\'
                ? '-'
                : character));
        string parentPath = Directory.GetParent(repository.Path)?.FullName ?? repository.Path;
        return Path.Combine(parentPath, $"{repository.Name}-{safeName}");
    }

    public RevisionRangeNavigationArgs? CreateSelectedBranchCommitRange(bool compareBothSides)
    {
        if (SelectedBranch is null) return null;
        string reference = SelectedBranch.IsRemote
            ? $"refs/remotes/{SelectedBranch.Name}"
            : $"refs/heads/{SelectedBranch.Name}";
        string currentName = _mainWindowViewModel.CurrentBranch;
        return new RevisionRangeNavigationArgs(
            compareBothSides
                ? string.Format(_localizationService.GetString("CompareBranchRangeTitle"), SelectedBranch.Name, currentName)
                : string.Format(_localizationService.GetString("BranchCommitRangeTitle"), SelectedBranch.Name),
            compareBothSides
                ? string.Format(_localizationService.GetString("CompareBranchRangeDescription"), SelectedBranch.Name, currentName)
                : string.Format(_localizationService.GetString("BranchCommitRangeDescription"), SelectedBranch.Name, currentName),
            _localizationService.GetString("NoBranchRangeCommits"),
            compareBothSides ? $"HEAD...{reference}" : $"HEAD..{reference}",
            compareBothSides ? "HEAD" : "",
            compareBothSides ? reference : "",
            compareBothSides ? currentName : "",
            compareBothSides ? SelectedBranch.Name : "",
            compareBothSides
                ? CommitRangeCherryPickScope.RightSide
                : CommitRangeCherryPickScope.AllCommits);
    }

    private void SelectBranches()
    {
        if (ReferenceKind == ReferenceListKind.Branches)
        {
            OnPropertyChanged(nameof(ReferenceKind));
            return;
        }

        ReferenceKind = ReferenceListKind.Branches;
    }

    private async Task SelectTagsAsync()
    {
        if (ReferenceKind == ReferenceListKind.Tags)
        {
            OnPropertyChanged(nameof(ReferenceKind));
        }
        else
        {
            ReferenceKind = ReferenceListKind.Tags;
        }

        await EnsureTagsLoadedAsync();
        if (BranchScope == BranchListScope.Remote)
        {
            await EnsureRemoteTagsLoadedAsync();
        }
    }

    private void OpenSelectedBranchChanges()
    {
        RevisionDiffNavigationArgs? arguments = CreateSelectedBranchDiff();
        if (arguments is not null)
        {
            _mainWindowViewModel.RequestNavigation(AppNavigationTarget.CommitRange, arguments);
        }
    }

    private void ShowSelectedBranchCommits(bool compareBothSides)
    {
        RevisionRangeNavigationArgs? arguments = CreateSelectedBranchCommitRange(compareBothSides);
        if (arguments is not null)
        {
            _mainWindowViewModel.RequestNavigation(AppNavigationTarget.CommitRange, arguments);
        }
    }

    private void ShowSelectedTagCommits()
    {
        RevisionRangeNavigationArgs? arguments = CreateSelectedTagCommitRange();
        if (arguments is not null)
        {
            _mainWindowViewModel.RequestNavigation(AppNavigationTarget.CommitRange, arguments);
        }
    }

    public RevisionDiffNavigationArgs? CreateSelectedBranchDiff()
    {
        if (SelectedBranch is null) return null;
        string reference = SelectedBranch.IsRemote
            ? $"refs/remotes/{SelectedBranch.Name}"
            : $"refs/heads/{SelectedBranch.Name}";
        string currentName = _mainWindowViewModel.CurrentBranch;
        return new RevisionDiffNavigationArgs(
            string.Format(_localizationService.GetString("BranchDiffRangeTitle"), currentName, SelectedBranch.Name),
            string.Format(_localizationService.GetString("BranchDiffRangeDescription"), currentName, SelectedBranch.Name),
            "HEAD",
            reference);
    }

    public CommitDiffNavigationArgs? CreateCommitChangesDiffArgs()
    {
        if (SelectedBranch is null) return null;
        string reference = SelectedBranch.IsRemote
            ? $"refs/remotes/{SelectedBranch.Name}"
            : $"refs/heads/{SelectedBranch.Name}";
        return new CommitDiffNavigationArgs(
            string.Format(_localizationService.GetString("LastCommitBranchChangesTitle"), SelectedBranch.Name),
            string.Format(_localizationService.GetString("LastCommitBranchChangesDescription"), SelectedBranch.ShortCommitHash, SelectedBranch.LastCommitMessage),
            SelectedBranchCommit);
    }


    public RevisionRangeNavigationArgs? CreateSelectedTagCommitRange()
    {
        if (SelectedTag is null || SelectedTagRelationDetails is null)
        {
            return null;
        }

        string reference = SelectedTag.IsRemote
            ? SelectedTag.ObjectHash
            : $"refs/tags/{SelectedTag.Name}^{{commit}}";
        string currentName = _mainWindowViewModel.CurrentBranch;
        return new RevisionRangeNavigationArgs(
            string.Format(_localizationService.GetString("CompareTagRangeTitle"), SelectedTag.Name, currentName),
            string.Format(_localizationService.GetString("CompareTagRangeDescription"), SelectedTag.Name, currentName),
            _localizationService.GetString("NoTagRangeCommits"),
            $"HEAD...{reference}",
            "HEAD",
            reference,
            currentName,
            SelectedTag.Name,
            CommitRangeCherryPickScope.RightSide);
    }

    private void CancelRemoteOperation()
    {
        _remoteOperationCancellationTokenSource?.Cancel();
        CancelRemoteOperationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCancelRemoteOperation));
        OnPropertyChanged(nameof(CancelRemoteOperationVisibility));
        PublishBranchesOperationState();
    }

    private async Task RunRemoteOperationAsync(
        string progressMessage,
        Func<CancellationToken, Task> operation)
    {
        await _gitService.ExecuteAsync(async () =>
        {
            using CancellationTokenSource cancellationTokenSource = new();
            _remoteOperationCancellationTokenSource = cancellationTokenSource;
            IsGitOperationRunning = true;
            ProgressMessage = progressMessage;
            CancelRemoteOperationCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanCancelRemoteOperation));
            OnPropertyChanged(nameof(CancelRemoteOperationVisibility));
            PublishBranchesOperationState();
            try
            {
                await operation(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                ShowInfo(_localizationService.GetString("RemoteOperationCanceled"));
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitRemoteOperationException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitRemoteCommandFailed"), exception.Message);
            }
            finally
            {
                _remoteOperationCancellationTokenSource = null;
                IsGitOperationRunning = false;
                CancelRemoteOperationCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanCancelRemoteOperation));
                OnPropertyChanged(nameof(CancelRemoteOperationVisibility));
                PublishBranchesOperationState();
            }
        });
    }

    private void PublishBranchesOperationState()
    {
        PublishOperationState(
            IsOperationProgressRunning,
            ProgressMessage,
            CanCancelRemoteOperation ? CancelRemoteOperationCommand : null);
    }

    private async Task LoadSelectedBranchCommitAsync(GitBranch? branch)
    {
        if (branch is null || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedBranchCommit = null;
            return;
        }

        string branchName = branch.Name;
        try
        {
            GitCommit commit = await _gitService.ReferenceDetails.GetBranchCommitAsync(_mainWindowViewModel.CurrentRepository, branch);
            if (SelectedBranch?.Name == branchName) SelectedBranchCommit = commit;
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedBranch?.Name == branchName) SelectedBranchCommit = null;
        }
    }

    private async Task LoadSelectedBranchComparisonAsync(GitBranch? branch)
    {
        if (branch is null || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedBranchDetails = null;
            return;
        }

        string branchName = branch.Name;
        try
        {
            GitBranchDetails details = await _gitService.ReferenceDetails.GetBranchComparisonAsync(_mainWindowViewModel.CurrentRepository, branch);
            if (SelectedBranch?.Name == branchName) SelectedBranchDetails = details;
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedBranch?.Name == branchName) SelectedBranchDetails = null;
        }
    }

    private async Task LoadSelectedBranchWorktreesAsync(GitBranch? branch)
    {
        if (branch is null || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedBranchWorktrees = null;
            return;
        }

        string branchName = branch.Name;
        try
        {
            IReadOnlyList<GitWorktree> worktrees = await _gitService.ReferenceDetails.GetBranchWorktreesAsync(
                _mainWindowViewModel.CurrentRepository,
                branch);
            if (SelectedBranch?.Name == branchName) SelectedBranchWorktrees = worktrees;
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedBranch?.Name == branchName) SelectedBranchWorktrees = null;
        }
    }

    private async Task LoadSelectedTagDetailsAsync(GitTag? tag)
    {
        if (tag is null || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedTagDetails = null;
            return;
        }

        GitTag detailsTag = tag;
        if (tag.IsRemote)
        {
            GitTag? localTag = Tags.FirstOrDefault(item => item.Name == tag.RemoteTagName
                && item.ReferenceObjectHash.Equals(tag.ReferenceObjectHash, StringComparison.OrdinalIgnoreCase));
            detailsTag = localTag ?? tag;
        }

        string tagName = tag.Name;
        try
        {
            GitTagDetails details = await _gitService.ReferenceDetails.GetTagDetailsAsync(_mainWindowViewModel.CurrentRepository, detailsTag);
            if (SelectedTag?.Name == tagName) SelectedTagDetails = details;
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedTag?.Name == tagName) SelectedTagDetails = null;
        }
    }

    private async Task LoadSelectedTagSignatureAsync(GitTag? tag)
    {
        if (tag?.IsAnnotated != true || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedTagSignatureDetails = null;
            _isSelectedTagSignatureLoaded = true;
            NotifySelectedDetailsChanged();
            return;
        }

        GitTag detailsTag = tag;
        if (tag.IsRemote)
        {
            GitTag? localTag = Tags.FirstOrDefault(item => item.Name == tag.RemoteTagName
                && item.ReferenceObjectHash.Equals(tag.ReferenceObjectHash, StringComparison.OrdinalIgnoreCase));
            detailsTag = localTag ?? tag;
        }

        string tagName = tag.Name;
        try
        {
            GitTagSignatureDetails details = await _gitService.ReferenceDetails.GetTagSignatureAsync(
                _mainWindowViewModel.CurrentRepository,
                detailsTag);
            if (SelectedTag?.Name == tagName)
            {
                SelectedTagSignatureDetails = details;
                _isSelectedTagSignatureLoaded = true;
                NotifySelectedDetailsChanged();
            }
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedTag?.Name == tagName)
            {
                SelectedTagSignatureDetails = null;
                _isSelectedTagSignatureLoaded = true;
                NotifySelectedDetailsChanged();
            }
        }
    }

    private async Task LoadSelectedBranchHistoryAsync(GitBranch? branch)
    {
        if (branch?.IsLocal != true || _mainWindowViewModel.CurrentRepository is null)
        {
            _selectedBranchReflogEntries = [];
            _isSelectedBranchHistoryLoaded = true;
            RebuildVisibleBranchHistory();
            NotifySelectedDetailsChanged();
            return;
        }

        string branchName = branch.Name;
        try
        {
            IReadOnlyList<GitReflogEntry> entries = await _gitService.ReferenceDetails.GetBranchReflogAsync(
                _mainWindowViewModel.CurrentRepository,
                branch);
            if (SelectedBranch?.Name == branchName)
            {
                _selectedBranchReflogEntries = entries;
                _isSelectedBranchHistoryLoaded = true;
                _showAllBranchHistory = false;
                RebuildVisibleBranchHistory();
                NotifySelectedDetailsChanged();
            }
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedBranch?.Name == branchName)
            {
                _selectedBranchReflogEntries = [];
                _isSelectedBranchHistoryLoaded = true;
                RebuildVisibleBranchHistory();
                NotifySelectedDetailsChanged();
            }
        }
    }

    private void ToggleBranchHistory()
    {
        _showAllBranchHistory = !_showAllBranchHistory;
        RebuildVisibleBranchHistory();
        OnPropertyChanged(nameof(BranchHistoryToggleText));
    }

    private void RebuildVisibleBranchHistory()
    {
        SelectedBranchHistoryEntries.Clear();
        IEnumerable<GitReflogEntry> entries = _showAllBranchHistory
            ? _selectedBranchReflogEntries
            : _selectedBranchReflogEntries.Take(BranchHistoryPreviewCount);
        foreach (GitReflogEntry entry in entries)
        {
            string actor = string.IsNullOrWhiteSpace(entry.ActorEmail)
                ? entry.ActorName
                : $"{entry.ActorName} <{entry.ActorEmail}>";
            string metadata = string.IsNullOrWhiteSpace(actor)
                ? FormatReflogDate(entry.OccurredAt)
                : $"{FormatReflogDate(entry.OccurredAt)} · {actor}";
            string previousHash = string.IsNullOrWhiteSpace(entry.ShortPreviousHash)
                ? _localizationService.GetString("ReflogPreviousHashUnknown")
                : entry.ShortPreviousHash;
            SelectedBranchHistoryEntries.Add(new BranchReflogDisplayItem(
                GetReflogActionText(entry.Subject),
                GetReflogDetails(entry.Subject),
                metadata,
                $"{previousHash} → {entry.ShortNewHash}"));
        }

        OnPropertyChanged(nameof(HasMoreBranchHistoryEntries));
        OnPropertyChanged(nameof(BranchHistoryToggleText));
    }

    private string GetReflogActionText(string subject)
    {
        string resourceKey = subject switch
        {
            _ when subject.StartsWith("commit", StringComparison.OrdinalIgnoreCase) => "ReflogActionCommit",
            _ when subject.StartsWith("reset", StringComparison.OrdinalIgnoreCase) => "ReflogActionReset",
            _ when subject.StartsWith("rebase", StringComparison.OrdinalIgnoreCase) => "ReflogActionRebase",
            _ when subject.StartsWith("merge", StringComparison.OrdinalIgnoreCase) => "ReflogActionMerge",
            _ when subject.StartsWith("branch", StringComparison.OrdinalIgnoreCase) => "ReflogActionBranch",
            _ when subject.StartsWith("pull", StringComparison.OrdinalIgnoreCase) => "ReflogActionPull",
            _ when subject.StartsWith("cherry-pick", StringComparison.OrdinalIgnoreCase) => "ReflogActionCherryPick",
            _ when subject.StartsWith("revert", StringComparison.OrdinalIgnoreCase) => "ReflogActionRevert",
            _ => "ReflogActionOther"
        };
        return _localizationService.GetString(resourceKey);
    }

    private static string GetReflogDetails(string subject)
    {
        int separatorIndex = subject.IndexOf(':');
        return separatorIndex >= 0 && separatorIndex < subject.Length - 1
            ? subject[(separatorIndex + 1)..].Trim()
            : subject;
    }

    private static string FormatReflogDate(DateTimeOffset? date)
    {
        return date?.ToString("g", CultureInfo.CurrentCulture) ?? "";
    }

    private string GetSignatureTypeText(GitSignatureType signatureType)
    {
        string resourceKey = signatureType switch
        {
            GitSignatureType.OpenPgp => "TagSignatureTypeOpenPgp",
            GitSignatureType.Ssh => "TagSignatureTypeSsh",
            GitSignatureType.X509 => "TagSignatureTypeX509",
            _ => "TagSignatureTypeUnknown"
        };
        return _localizationService.GetString(resourceKey);
    }

    private async Task LoadSelectedTagRelationAsync(GitTag? tag)
    {
        if (tag is null || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedTagRelationDetails = null;
            _isSelectedTagRelationLoaded = true;
            OnPropertyChanged(nameof(SelectedTagRelationText));
            return;
        }

        string tagName = tag.Name;
        GitTag detailsTag = tag;
        if (tag.IsRemote)
        {
            GitTag? localTag = Tags.FirstOrDefault(item => item.Name == tag.RemoteTagName
                && item.ReferenceObjectHash.Equals(tag.ReferenceObjectHash, StringComparison.OrdinalIgnoreCase));
            detailsTag = localTag ?? tag;
        }

        try
        {
            GitTagRelationDetails details = await _gitService.ReferenceDetails.GetTagRelationAsync(
                _mainWindowViewModel.CurrentRepository,
                detailsTag);
            if (SelectedTag?.Name == tagName)
            {
                SelectedTagRelationDetails = details;
                _isSelectedTagRelationLoaded = true;
                NotifySelectedDetailsChanged();
            }
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedTag?.Name == tagName)
            {
                SelectedTagRelationDetails = null;
                _isSelectedTagRelationLoaded = true;
                NotifySelectedDetailsChanged();
            }
        }
    }

    private async Task LoadSelectedTagWorktreesAsync(GitTag? tag)
    {
        if (tag is null || _mainWindowViewModel.CurrentRepository is null)
        {
            SelectedTagWorktrees = null;
            return;
        }

        string tagName = tag.Name;
        try
        {
            IReadOnlyList<GitWorktree> worktrees = await _gitService.ReferenceDetails.GetTagWorktreesAsync(
                _mainWindowViewModel.CurrentRepository,
                tag);
            if (SelectedTag?.Name == tagName) SelectedTagWorktrees = worktrees;
        }
        catch (Exception exception) when (exception is GitCommandException or FileNotFoundException or DirectoryNotFoundException)
        {
            if (SelectedTag?.Name == tagName) SelectedTagWorktrees = null;
        }
    }

    private void NotifySelectedDetailsChanged()
    {
        OnPropertyChanged(nameof(HasSelectedBranch));
        OnPropertyChanged(nameof(HasSelectedTag));
        OnPropertyChanged(nameof(IsSelectedBranchLocal));
        OnPropertyChanged(nameof(IsSelectedBranchRemote));
        OnPropertyChanged(nameof(HasSelectedBranchDescription));
        OnPropertyChanged(nameof(CanEditSelectedBranchDescription));
        OnPropertyChanged(nameof(CanAddSelectedBranchDescriptionVisibility));
        OnPropertyChanged(nameof(CanEditSelectedBranchDescriptionVisibility));
        OnPropertyChanged(nameof(CanDeleteSelectedBranchDescription));
        OnPropertyChanged(nameof(SelectedBranchScopeText));
        OnPropertyChanged(nameof(SelectedBranchDescriptionSeparatorText));
        OnPropertyChanged(nameof(SelectedTagScopeText));
        OnPropertyChanged(nameof(SelectedTagTypeText));
        OnPropertyChanged(nameof(IsSelectedTagAnnotated));
        OnPropertyChanged(nameof(SelectedReferenceCommit));
        OnPropertyChanged(nameof(SelectedCommitTitle));
        OnPropertyChanged(nameof(SelectedCommitAuthor));
        OnPropertyChanged(nameof(SelectedCommitDate));
        OnPropertyChanged(nameof(SelectedCommitHash));
        OnPropertyChanged(nameof(SelectedCommitMessage));
        OnPropertyChanged(nameof(SelectedBranchSynchronization));
        OnPropertyChanged(nameof(SelectedBranchSynchronizationTitle));
        OnPropertyChanged(nameof(SelectedBranchSynchronizationText));
        OnPropertyChanged(nameof(SelectedBranchIncomingStatusText));
        OnPropertyChanged(nameof(SelectedBranchOutgoingStatusText));
        OnPropertyChanged(nameof(CanSetSelectedBranchUpstream));
        OnPropertyChanged(nameof(CanSetSelectedBranchPushRemote));
        OnPropertyChanged(nameof(CanUseRemoteSelector));
        OnPropertyChanged(nameof(SelectedRemoteBranchLocalName));
        OnPropertyChanged(nameof(IsSelectedRemoteBranchMissingLocal));
        OnPropertyChanged(nameof(SelectedBranchRelationText));
        OnPropertyChanged(nameof(SelectedBranchDiffText));
        OnPropertyChanged(nameof(SelectedBranchMergeBaseText));
        OnPropertyChanged(nameof(SelectedBranchMergeCapabilityText));
        OnPropertyChanged(nameof(CanOpenSelectedBranchChanges));
        OnPropertyChanged(nameof(CanOpenLastCommitBranchChanges));
        OnPropertyChanged(nameof(CanShowSelectedBranchCommits));
        OnPropertyChanged(nameof(CanCompareSelectedBranch));
        OnPropertyChanged(nameof(HasSelectedBranchWorktree));
        OnPropertyChanged(nameof(IsSelectedBranchInOtherWorktree));
        OnPropertyChanged(nameof(CanCreateSelectedBranchWorktree));
        OnPropertyChanged(nameof(CanOpenSelectedBranchWorktree));
        OnPropertyChanged(nameof(BranchPrimaryActionText));
        OnPropertyChanged(nameof(SelectedBranchWorktreePath));
        OnPropertyChanged(nameof(SelectedBranchHistorySummary));
        OnPropertyChanged(nameof(SelectedBranchHistoryAvailabilityText));
        OnPropertyChanged(nameof(HasMoreBranchHistoryEntries));
        OnPropertyChanged(nameof(BranchHistoryToggleText));
        OnPropertyChanged(nameof(HasSelectedTagWorktrees));
        OnPropertyChanged(nameof(SelectedTagWorktreeText));
        OnPropertyChanged(nameof(SelectedTaggerText));
        OnPropertyChanged(nameof(SelectedTaggerDate));
        OnPropertyChanged(nameof(SelectedTagMessage));
        OnPropertyChanged(nameof(SelectedTagTargetType));
        OnPropertyChanged(nameof(SelectedTagTargetCommitText));
        OnPropertyChanged(nameof(SelectedTagSignatureSummary));
        OnPropertyChanged(nameof(SelectedTagSignatureDetailsText));
        OnPropertyChanged(nameof(SelectedTagRelationText));
        OnPropertyChanged(nameof(SelectedTagMergeBaseText));
        OnPropertyChanged(nameof(SelectedTagContainingBranchesText));
        OnPropertyChanged(nameof(CanShowSelectedTagCommits));
        OnPropertyChanged(nameof(SelectedTagRemoteStatusText));
        OnPropertyChanged(nameof(SelectedTagRemoteStatusTitle));
        OnPropertyChanged(nameof(IsSelectedRemoteTagMissingLocal));
        OnPropertyChanged(nameof(SelectedBranchName));
        OnPropertyChanged(nameof(SelectedTagName));
    }

    private void UpdateCommandStates()
    {
        RefreshBranchesCommand.NotifyCanExecuteChanged();
        CreateReferenceCommand.NotifyCanExecuteChanged();
        FetchBranchCommand.NotifyCanExecuteChanged();
        FetchTagCommand.NotifyCanExecuteChanged();
        SetBranchUpstreamCommand.NotifyCanExecuteChanged();
        SetBranchPushRemoteCommand.NotifyCanExecuteChanged();
        CancelRemoteOperationCommand.NotifyCanExecuteChanged();
        CheckoutBranchCommand.NotifyCanExecuteChanged();
        BranchPrimaryCommand.NotifyCanExecuteChanged();
        CheckoutContextBranchCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        RenameBranchCommand.NotifyCanExecuteChanged();
        RenameContextBranchCommand.NotifyCanExecuteChanged();
        EditBranchDescriptionCommand.NotifyCanExecuteChanged();
        EditContextBranchDescriptionCommand.NotifyCanExecuteChanged();
        DeleteBranchDescriptionCommand.NotifyCanExecuteChanged();
        DeleteContextBranchDescriptionCommand.NotifyCanExecuteChanged();
        DeleteBranchCommand.NotifyCanExecuteChanged();
        DeleteContextBranchCommand.NotifyCanExecuteChanged();
        CreateTagCommand.NotifyCanExecuteChanged();
        DeleteTagCommand.NotifyCanExecuteChanged();
        DeleteContextTagCommand.NotifyCanExecuteChanged();
        CheckoutTagCommand.NotifyCanExecuteChanged();
        CheckoutContextTagCommand.NotifyCanExecuteChanged();
        CreateBranchFromTagCommand.NotifyCanExecuteChanged();
        CreateBranchFromContextTagCommand.NotifyCanExecuteChanged();
        MergeBranchCommand.NotifyCanExecuteChanged();
        SquashMergeBranchCommand.NotifyCanExecuteChanged();
        PrepareBranchSnapshotCommand.NotifyCanExecuteChanged();
        RebaseBranchCommand.NotifyCanExecuteChanged();
        CreateBranchWorktreeCommand.NotifyCanExecuteChanged();
        OpenBranchWorktreeCommand.NotifyCanExecuteChanged();
        CreateTagWorktreeCommand.NotifyCanExecuteChanged();
        CreateTagBranchWorktreeCommand.NotifyCanExecuteChanged();
        OpenTagWorktreeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCheckoutSelectedBranch));
        OnPropertyChanged(nameof(CanDeleteSelectedBranch));
        OnPropertyChanged(nameof(CanRenameSelectedBranch));
        OnPropertyChanged(nameof(CanEditSelectedBranchDescription));
        OnPropertyChanged(nameof(CanDeleteSelectedBranchDescription));
        OnPropertyChanged(nameof(CanMergeSelectedBranch));
        OnPropertyChanged(nameof(CanPrepareSelectedBranchSnapshot));
        OnPropertyChanged(nameof(CanCreateBranch));
        OnPropertyChanged(nameof(CanCreateTag));
        OnPropertyChanged(nameof(CanCreateReference));
        OnPropertyChanged(nameof(CanFetchSelectedBranch));
        OnPropertyChanged(nameof(CanFetchSelectedTag));
        OnPropertyChanged(nameof(CanSetSelectedBranchUpstream));
        OnPropertyChanged(nameof(CanSetSelectedBranchPushRemote));
        OnPropertyChanged(nameof(CanDeleteSelectedTag));
        OnPropertyChanged(nameof(CanCheckoutSelectedTag));
        OnPropertyChanged(nameof(CanCreateBranchFromSelectedTag));
    }

    private void SelectBranch(GitBranch branch)
    {
        SelectedBranch = branch;
    }

    private void ApplyBranchFilter(string? preferredBranchName = null)
    {
        string? selectedBranchName = preferredBranchName ?? SelectedBranch?.Name;
        var source = BranchScope switch
        {
            BranchListScope.Remote => RemoteBranches.AsEnumerable(),
            BranchListScope.All => Branches.Concat(RemoteBranches),
            _ => Branches.AsEnumerable()
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            source = source.Where(MatchesSearchText);
        }

        var filteredBranches = source.ToList();
        FilteredBranches.Clear();
        foreach (var branch in filteredBranches)
        {
            FilteredBranches.Add(branch);
        }

        SelectedBranch = FilteredBranches.FirstOrDefault(branch => branch.Name == selectedBranchName)
            ?? FilteredBranches.FirstOrDefault(branch => branch.IsCurrent)
            ?? FilteredBranches.FirstOrDefault();

        NotifyReferenceListChanged();
    }

    private void UpdateBranchDescription(string branchName, string description)
    {
        int branchIndex = Branches.ToList().FindIndex(item => item.Name == branchName);
        if (branchIndex < 0)
        {
            return;
        }

        GitBranch updatedBranch = Branches[branchIndex].WithConfigDescription(description);
        Branches[branchIndex] = updatedBranch;
        ApplyBranchFilter(branchName);
        SelectedBranch = FilteredBranches.FirstOrDefault(item => item.Name == branchName) ?? updatedBranch;
    }

    private void ApplyTagFilter(string? preferredTagName = null)
    {
        string? selectedTagName = preferredTagName ?? SelectedTag?.Name;
        var source = BranchScope switch
        {
            BranchListScope.Remote => RemoteTags.AsEnumerable(),
            BranchListScope.All => Tags.Concat(RemoteTags),
            _ => Tags.AsEnumerable()
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            source = source.Where(MatchesSearchText);
        }

        var filteredTags = source.ToList();
        FilteredTags.Clear();
        foreach (var tag in filteredTags)
        {
            FilteredTags.Add(tag);
        }

        SelectedTag = FilteredTags.FirstOrDefault(tag => tag.Name == selectedTagName)
            ?? FilteredTags.FirstOrDefault();

        NotifyReferenceListChanged();
    }

    private bool MatchesSearchText(GitBranch branch)
    {
        return branch.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || branch.ShortCommitHash.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || branch.LastCommitMessage.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSearchText(GitTag tag)
    {
        return tag.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || tag.ShortCommitHash.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || tag.Subject.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || tag.TypeLabel.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void ReplaceLocalTags(
        IReadOnlyList<GitTag> tags,
        string? headCommitHash,
        string? preferredTagName = null)
    {
        Tags.Clear();
        foreach (GitTag tag in tags)
        {
            Tags.Add(tag.WithCurrentState(IsTagAtHead(tag, headCommitHash)));
        }

        HasNoTags = Tags.Count == 0 && RemoteTags.Count == 0;
        ApplyTagFilter(preferredTagName);
    }

    private void ReplaceRemoteTags(
        IReadOnlyList<GitTag> tags,
        string? headCommitHash,
        string? preferredTagName = null)
    {
        string? selectedTagName = preferredTagName ?? SelectedTag?.Name;
        IReadOnlyDictionary<string, GitTag> localTagsByName = Tags.ToDictionary(
            tag => tag.Name,
            StringComparer.Ordinal);
        RemoteTags.Clear();
        foreach (GitTag tag in tags)
        {
            localTagsByName.TryGetValue(tag.RemoteTagName, out GitTag? localTag);
            GitTag displayTag = tag.WithListMetadataFromMatchingLocalTag(localTag);
            RemoteTags.Add(displayTag.WithCurrentState(IsTagAtHead(displayTag, headCommitHash)));
        }

        HasNoTags = Tags.Count == 0 && RemoteTags.Count == 0;
        ApplyTagFilter(selectedTagName);
    }

    private IReadOnlyList<GitTag> GetCachedRemoteTags()
    {
        return Remotes
            .SelectMany(remote => _remoteTagCache.TryGetValue(
                remote.Name,
                out IReadOnlyList<GitTag>? tags)
                ? tags
                : [])
            .OrderBy(tag => tag.RemoteName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag.RemoteTagName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ResetLazyDataState()
    {
        _areLocalTagsLoaded = false;
        _tagHeadCommitHash = null;
        _remoteTagCache.Clear();
        _remoteTagLoadAttempts.Clear();
        SynchronizationSnapshot = null;
        RemoteBranchSynchronizationSnapshot = null;
        _remoteBranchSynchronizationRemoteName = "";
        _lastRepositoryFetch = null;
        _isLastRepositoryFetchLoaded = false;
        OnPropertyChanged(nameof(LastRepositoryFetchText));
        SelectedBranchCommit = null;
        SelectedBranchDetails = null;
        SelectedBranchWorktrees = null;
        _selectedBranchReflogEntries = [];
        _isSelectedBranchHistoryLoaded = false;
        _showAllBranchHistory = false;
        SelectedBranchHistoryEntries.Clear();
        SelectedTagDetails = null;
        SelectedTagSignatureDetails = null;
        _isSelectedTagSignatureLoaded = false;
        SelectedTagRelationDetails = null;
        _isSelectedTagRelationLoaded = false;
        SelectedTagWorktrees = null;
        Tags.Clear();
        RemoteTags.Clear();
        FilteredTags.Clear();
        SelectedTag = null;
        HasNoTags = false;
    }

    private static bool IsTagAtHead(GitTag tag, string? headCommitHash)
    {
        return headCommitHash is not null
            && tag.ObjectHash.Equals(headCommitHash, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncSelectedBranchLists(GitBranch? branch)
    {
        _selectedLocalBranch = branch?.IsLocal == true ? branch : null;
        _selectedRemoteBranch = branch?.IsRemote == true ? branch : null;
        OnPropertyChanged(nameof(SelectedLocalBranch));
        OnPropertyChanged(nameof(SelectedRemoteBranch));
    }

    private void SyncSelectedBranchRemoteSettings()
    {
        _isSynchronizingBranchRemoteSettings = true;
        try
        {
            BranchSynchronizationItem? item = SelectedBranchSynchronization;
            List<BranchRemoteOption> upstreamRemoteOptions =
            [
                new(
                    _localizationService.GetString("BranchRemoteAutomaticOption"),
                    remoteName: null)
            ];
            upstreamRemoteOptions.AddRange(
                Remotes.Select(remote => new BranchRemoteOption(remote.Name, remote.Name)));

            string explicitUpstreamRemoteName = item?.HasUpstream == true
                ? item.UpstreamRemoteName
                : "";
            AddMissingRemoteOption(upstreamRemoteOptions, explicitUpstreamRemoteName);
            BranchUpstreamRemoteOptions = upstreamRemoteOptions;
            SelectedBranchUpstreamRemoteOption = upstreamRemoteOptions.First(option => string.Equals(
                option.RemoteName ?? "",
                explicitUpstreamRemoteName,
                StringComparison.Ordinal));

            List<BranchRemoteOption> pushRemoteOptions =
            [
                new(
                    _localizationService.GetString("BranchRemoteAutomaticOption"),
                    remoteName: null)
            ];
            pushRemoteOptions.AddRange(
                Remotes.Select(remote => new BranchRemoteOption(remote.Name, remote.Name)));

            string explicitPushRemoteName = item?.ExplicitPushRemoteName ?? "";
            AddMissingRemoteOption(pushRemoteOptions, explicitPushRemoteName);

            BranchPushRemoteOptions = pushRemoteOptions;
            SelectedBranchPushRemoteOption = pushRemoteOptions.First(option => string.Equals(
                option.RemoteName ?? "",
                explicitPushRemoteName,
                StringComparison.Ordinal));
        }
        finally
        {
            _isSynchronizingBranchRemoteSettings = false;
        }
    }

    private void AddMissingRemoteOption(
        List<BranchRemoteOption> options,
        string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName)
            || options.Any(option => string.Equals(
                option.RemoteName,
                remoteName,
                StringComparison.Ordinal)))
        {
            return;
        }

        options.Add(new BranchRemoteOption(
            string.Format(
                _localizationService.GetString("BranchRemoteMissingOption"),
                remoteName),
            remoteName));
    }

    private void ApplyCurrentFilter()
    {
        if (ReferenceKind == ReferenceListKind.Tags)
        {
            ApplyTagFilter();
        }
        else
        {
            ApplyBranchFilter();
        }
    }

    private void NotifyReferenceModeChanged()
    {
        OnPropertyChanged(nameof(BranchesModeVisibility));
        OnPropertyChanged(nameof(TagsModeVisibility));
        OnPropertyChanged(nameof(ReferenceListTitle));
        NotifyReferenceListChanged();
        NotifySelectedDetailsChanged();
    }

    private static string FormatDisplayDate(string value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out DateTimeOffset date)
            ? date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : value;
    }

    private string GetSelectedRemoteName()
    {
        if (ReferenceKind == ReferenceListKind.Branches && SelectedBranch?.IsRemote == true)
        {
            int separatorIndex = SelectedBranch.Name.IndexOf('/');
            if (separatorIndex > 0)
            {
                return SelectedBranch.Name[..separatorIndex];
            }
        }

        if (ReferenceKind == ReferenceListKind.Tags &&
            SelectedTag?.IsRemote == true &&
            !string.IsNullOrWhiteSpace(SelectedTag.RemoteName))
        {
            return SelectedTag.RemoteName;
        }

        if (!string.IsNullOrWhiteSpace(_mainWindowViewModel.SelectedRemoteName))
        {
            return _mainWindowViewModel.SelectedRemoteName;
        }

        return SelectedRemote?.Name ?? _localizationService.GetString("SelectedRemoteFallbackName");
    }

    private static string GetLocalBranchName(GitBranch? branch)
    {
        if (branch is null)
        {
            return "";
        }

        int separatorIndex = branch.IsRemote ? branch.Name.IndexOf('/') : -1;
        return separatorIndex >= 0 && separatorIndex < branch.Name.Length - 1
            ? branch.Name[(separatorIndex + 1)..]
            : branch.Name;
    }

    private GitRemote? FindRemote(string remoteName)
    {
        return Remotes.FirstOrDefault(remote => string.Equals(
            remote.Name,
            remoteName,
            StringComparison.Ordinal));
    }

    private static string GetRemoteNameFromReference(string referenceName)
    {
        int separatorIndex = referenceName.IndexOf('/');
        return separatorIndex > 0 ? referenceName[..separatorIndex] : "";
    }

    private GitTag? FindLocalTag(GitTag? tag)
    {
        if (tag is null)
        {
            return null;
        }

        string localName = tag.IsRemote ? tag.RemoteTagName : tag.Name;
        return Tags.FirstOrDefault(item => item.Name.Equals(localName, StringComparison.Ordinal));
    }

    private void NotifyReferenceListChanged()
    {
        OnPropertyChanged(nameof(ReferencesVisible));
        OnPropertyChanged(nameof(BranchesVisible));
        OnPropertyChanged(nameof(TagsVisible));
        OnPropertyChanged(nameof(FilteredBranchesVisible));
        OnPropertyChanged(nameof(FilteredTagsVisible));
        OnPropertyChanged(nameof(HasFilteredBranches));
        OnPropertyChanged(nameof(HasFilteredTags));
        OnPropertyChanged(nameof(HasNoFilteredBranches));
        OnPropertyChanged(nameof(HasNoFilteredTags));
        OnPropertyChanged(nameof(HasNoBranchesNotice));
        OnPropertyChanged(nameof(HasNoTagsNotice));
        OnPropertyChanged(nameof(ReferenceListTitle));
    }

    private async Task<System.Collections.Generic.IReadOnlyList<GitCommit>?> LoadCommitsForCreateDialogAsync()
    {
        if (_mainWindowViewModel.CurrentRepository is null)
        {
            ShowError(_localizationService.GetString("OpenRepositoryBeforeBranches"));
            return null;
        }

        System.Collections.Generic.IReadOnlyList<GitCommit>? commits = null;
        await RunGitOperationAsync(_localizationService.GetString("LoadingCommitRange"), async () =>
        {
            try
            {
                ClearResultMessages();
                commits = await _gitService.GetHistoryAsync(_mainWindowViewModel.CurrentRepository);
            }
            catch (FileNotFoundException)
            {
                ShowError(_localizationService.GetString("GitExecutableNotFound"));
            }
            catch (DirectoryNotFoundException)
            {
                ShowError(_localizationService.GetString("RepositoryFolderNotFound"));
            }
            catch (GitCommandException exception)
            {
                ShowError(_localizationService.GetString("GitLogCommandFailed"), exception.Message);
            }
        });

        return commits;
    }
}
