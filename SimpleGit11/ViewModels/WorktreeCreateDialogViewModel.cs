using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public sealed partial class WorktreeCreateDialogViewModel : ValidatableViewModelBase, IDisposable
{
    private static readonly GitRevisionKind[] AllRevisionKinds =
    [
        GitRevisionKind.Head,
        GitRevisionKind.Branch,
        GitRevisionKind.Tag,
        GitRevisionKind.Commit
    ];

    private static readonly GitRevisionKind[] ExistingBranchRevisionKinds =
    [
        GitRevisionKind.Branch
    ];

    private WorktreeCreationMode _creationMode;
    private readonly bool _canUseExistingBranch;
    private readonly string _nonDetachedSuggestedPath;
    private readonly DialogValidationMessages _validationMessages;
    private string _lastSuggestedPath;

    public WorktreeCreateDialogViewModel(
        GitRevisionSelectorViewModel revisionSelector,
        string path,
        string newBranchName,
        WorktreeCreationMode creationMode,
        bool canUseExistingBranch,
        DialogValidationMessages validationMessages)
    {
        RevisionSelector = revisionSelector;
        _validationMessages = validationMessages;
        ErrorsChanged += OnErrorsChanged;
        RevisionSelector.PropertyChanged += OnRevisionSelectorPropertyChanged;
        _canUseExistingBranch = canUseExistingBranch;
        _creationMode = !canUseExistingBranch && creationMode == WorktreeCreationMode.ExistingBranch
            ? WorktreeCreationMode.NewBranch
            : creationMode;
        _nonDetachedSuggestedPath = RemoveDetachedSuffix(path);
        Path = CreateSuggestedPath();
        _lastSuggestedPath = Path;
        NewBranchName = newBranchName;
        IsLocked = false;
        ConfigureRevisionKinds();
        ValidateAllProperties();
    }

    public GitRevisionSelectorViewModel RevisionSelector { get; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [CustomValidation(typeof(WorktreeCreateDialogViewModel), nameof(ValidateRequiredText))]
    public partial string Path { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [CustomValidation(typeof(WorktreeCreateDialogViewModel), nameof(ValidateNewBranchName))]
    public partial string NewBranchName { get; set; }

    public int SelectedModeIndex
    {
        get => (int)_creationMode;
        set
        {
            WorktreeCreationMode creationMode = (WorktreeCreationMode)value;
            if (creationMode == WorktreeCreationMode.ExistingBranch && !CanUseExistingBranch)
            {
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _creationMode, creationMode))
            {
                ConfigureRevisionKinds();
                UpdateSuggestedPath();
                OnPropertyChanged(nameof(CanSpecifyNewBranch));
                OnPropertyChanged(nameof(CanCreate));
                ValidateProperty(NewBranchName, nameof(NewBranchName));
            }
        }
    }

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    public string PathError => GetFirstValidationError(nameof(Path));

    public bool HasPathError => !string.IsNullOrEmpty(PathError);

    public string StartPointError => string.IsNullOrWhiteSpace(RevisionSelector.StartPoint)
        ? _validationMessages.RequiredField
        : "";

    public bool HasStartPointError => !string.IsNullOrEmpty(StartPointError);

    public string NewBranchNameError => GetFirstValidationError(nameof(NewBranchName));

    public bool HasNewBranchNameError => !string.IsNullOrEmpty(NewBranchNameError);

    public bool CanCreate => !HasErrors && RevisionSelector.CanResolve;

    public bool CanSpecifyNewBranch => _creationMode == WorktreeCreationMode.NewBranch;

    public bool CanUseExistingBranch => _canUseExistingBranch;

    public static ValidationResult? ValidateRequiredText(
        string? value,
        ValidationContext validationContext)
    {
        WorktreeCreateDialogViewModel viewModel =
            (WorktreeCreateDialogViewModel)validationContext.ObjectInstance;

        return string.IsNullOrWhiteSpace(value)
            ? new ValidationResult(viewModel._validationMessages.RequiredField)
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateNewBranchName(
        string? newBranchName,
        ValidationContext validationContext)
    {
        WorktreeCreateDialogViewModel viewModel =
            (WorktreeCreateDialogViewModel)validationContext.ObjectInstance;

        return viewModel._creationMode == WorktreeCreationMode.NewBranch
            && string.IsNullOrWhiteSpace(newBranchName)
                ? new ValidationResult(viewModel._validationMessages.RequiredField)
                : ValidationResult.Success;
    }

    public async Task<bool> ResolveStartPointAsync()
    {
        bool resolved = await RevisionSelector.ResolveAsync();
        if (resolved)
        {
            UpdateSuggestedPath();
        }

        return resolved;
    }

    public WorktreeCreationRequest CreateRequest()
    {
        string startPoint = _creationMode == WorktreeCreationMode.Detached
            ? RevisionSelector.ResolvedRevision!.CommitHash
            : RevisionSelector.StartPoint.Trim();
        return new WorktreeCreationRequest(
            Path.Trim(),
            startPoint,
            _creationMode == WorktreeCreationMode.NewBranch ? NewBranchName.Trim() : "",
            _creationMode == WorktreeCreationMode.Detached,
            IsLocked,
            _creationMode);
    }

    public void Dispose()
    {
        RevisionSelector.PropertyChanged -= OnRevisionSelectorPropertyChanged;
        RevisionSelector.Dispose();
    }

    private void ConfigureRevisionKinds()
    {
        if (_creationMode == WorktreeCreationMode.ExistingBranch)
        {
            RevisionSelector.ConfigureKinds(
                ExistingBranchRevisionKinds,
                GitRevisionKind.Branch,
                includeRemoteBranches: false);
        }
        else
        {
            RevisionSelector.ConfigureKinds(
                AllRevisionKinds,
                RevisionSelector.SelectedKind,
                includeRemoteBranches: true);
        }
    }

    private string CreateSuggestedPath()
    {
        if (_creationMode != WorktreeCreationMode.Detached)
        {
            return _nonDetachedSuggestedPath;
        }

        string shortHash = RevisionSelector.SelectedKind == GitRevisionKind.Commit
            ? RevisionSelector.SelectedSuggestion?.ShortHash
                ?? RevisionSelector.ResolvedRevision?.ShortHash
                ?? ""
            : "";
        return string.IsNullOrWhiteSpace(shortHash)
            ? $"{_nonDetachedSuggestedPath}-detach"
            : $"{_nonDetachedSuggestedPath}-detach-{shortHash}";
    }

    private void UpdateSuggestedPath()
    {
        if (!Path.Equals(_lastSuggestedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastSuggestedPath = CreateSuggestedPath();
        Path = _lastSuggestedPath;
    }

    private static string RemoveDetachedSuffix(string path)
    {
        int suffixIndex = path.LastIndexOf("-detach", StringComparison.OrdinalIgnoreCase);
        if (suffixIndex < 0)
        {
            return path;
        }

        string suffix = path[(suffixIndex + 7)..];
        return string.IsNullOrEmpty(suffix) || suffix.StartsWith("-", StringComparison.Ordinal)
            ? path[..suffixIndex]
            : path;
    }

    private void OnRevisionSelectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GitRevisionSelectorViewModel.StartPoint)
            or nameof(GitRevisionSelectorViewModel.CanResolve)
            or nameof(GitRevisionSelectorViewModel.IsLoading)
            or nameof(GitRevisionSelectorViewModel.IsValidating))
        {
            OnPropertyChanged(nameof(StartPointError));
            OnPropertyChanged(nameof(HasStartPointError));
            OnPropertyChanged(nameof(CanCreate));
        }

        if (e.PropertyName is nameof(GitRevisionSelectorViewModel.SelectedKind)
            or nameof(GitRevisionSelectorViewModel.SelectedSuggestion)
            or nameof(GitRevisionSelectorViewModel.ResolvedRevision))
        {
            UpdateSuggestedPath();
        }
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Path):
                OnPropertyChanged(nameof(PathError));
                OnPropertyChanged(nameof(HasPathError));
                break;
            case nameof(NewBranchName):
                OnPropertyChanged(nameof(NewBranchNameError));
                OnPropertyChanged(nameof(HasNewBranchNameError));
                break;
        }

        OnPropertyChanged(nameof(CanCreate));
    }
}
