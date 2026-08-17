using CommunityToolkit.Mvvm.ComponentModel;
using SimpleGit11.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SimpleGit11.ViewModels;

public sealed class OrphanBranchContentOption
{
    public OrphanBranchContentOption(OrphanBranchContentMode mode, string displayName)
    {
        Mode = mode;
        DisplayName = displayName;
    }

    public OrphanBranchContentMode Mode { get; }

    public string DisplayName { get; }
}

public sealed partial class BranchCreateDialogViewModel : ValidatableViewModelBase, IDisposable
{
    private readonly DialogValidationMessages _validationMessages;

    public BranchCreateDialogViewModel(
        GitRevisionSelectorViewModel revisionSelector,
        IReadOnlyList<OrphanBranchContentOption> orphanContentOptions,
        DialogValidationMessages validationMessages)
    {
        RevisionSelector = revisionSelector;
        _validationMessages = validationMessages;
        ErrorsChanged += OnErrorsChanged;
        RevisionSelector.PropertyChanged += OnRevisionSelectorPropertyChanged;

        foreach (OrphanBranchContentOption option in orphanContentOptions)
        {
            OrphanContentOptions.Add(option);
        }

        BranchName = "";
        SelectedOrphanContentOption = OrphanContentOptions.Count > 0
            ? OrphanContentOptions[0]
            : null;
        CheckoutBranch = true;
        ValidateAllProperties();
    }

    public GitRevisionSelectorViewModel RevisionSelector { get; }

    public ObservableCollection<OrphanBranchContentOption> OrphanContentOptions { get; } = [];

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [CustomValidation(typeof(BranchCreateDialogViewModel), nameof(ValidateBranchName))]
    public partial string BranchName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartPointEnabled))]
    [NotifyPropertyChangedFor(nameof(CopiesStartPointSnapshot))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial OrphanBranchContentOption? SelectedOrphanContentOption { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartPointEnabled))]
    [NotifyPropertyChangedFor(nameof(WillCheckoutOrphan))]
    [NotifyPropertyChangedFor(nameof(WillCreateOrphanWithoutCheckout))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial bool IsOrphan { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WillCheckoutOrphan))]
    [NotifyPropertyChangedFor(nameof(WillCreateOrphanWithoutCheckout))]
    public partial bool CheckoutBranch { get; set; }

    public bool CopiesStartPointSnapshot =>
        SelectedOrphanContentOption?.Mode == OrphanBranchContentMode.StartPointSnapshot;

    public bool IsStartPointEnabled => !IsOrphan || CopiesStartPointSnapshot;

    public bool CanEditStartPoint => IsStartPointEnabled && RevisionSelector.IsStartPointEnabled;

    public bool WillCheckoutOrphan => IsOrphan && CheckoutBranch;

    public bool WillCreateOrphanWithoutCheckout => IsOrphan && !CheckoutBranch;

    public string BranchNameError => GetFirstValidationError(nameof(BranchName));

    public bool HasBranchNameError => !string.IsNullOrEmpty(BranchNameError);

    public string StartPointError => IsStartPointEnabled
        && string.IsNullOrWhiteSpace(RevisionSelector.StartPoint)
            ? _validationMessages.SelectionRequired
            : "";

    public bool HasStartPointError => !string.IsNullOrEmpty(StartPointError);

    public bool CanCreate => !HasErrors
        && (!IsStartPointEnabled || RevisionSelector.CanResolve);

    public static ValidationResult? ValidateBranchName(
        string? branchName,
        ValidationContext validationContext)
    {
        BranchCreateDialogViewModel viewModel =
            (BranchCreateDialogViewModel)validationContext.ObjectInstance;

        return string.IsNullOrWhiteSpace(branchName)
            ? new ValidationResult(viewModel._validationMessages.RequiredField)
            : ValidationResult.Success;
    }

    public Task<bool> ResolveStartPointAsync()
    {
        return IsStartPointEnabled
            ? RevisionSelector.ResolveAsync()
            : Task.FromResult(true);
    }

    public BranchCreationRequest CreateRequest()
    {
        bool usesStartPoint = !IsOrphan || CopiesStartPointSnapshot;
        return new BranchCreationRequest(
            BranchName.Trim(),
            usesStartPoint ? RevisionSelector.ResolvedRevision!.CommitHash : null,
            GetCreationMode());
    }

    public void Dispose()
    {
        RevisionSelector.PropertyChanged -= OnRevisionSelectorPropertyChanged;
        RevisionSelector.Dispose();
    }

    partial void OnIsOrphanChanged(bool value)
    {
        NotifyStartPointStateChanged();
    }

    partial void OnSelectedOrphanContentOptionChanged(OrphanBranchContentOption? value)
    {
        NotifyStartPointStateChanged();
    }

    private BranchCreationMode GetCreationMode()
    {
        if (!IsOrphan)
        {
            return CheckoutBranch
                ? BranchCreationMode.CheckoutFromCommit
                : BranchCreationMode.FromCommit;
        }

        if (CopiesStartPointSnapshot)
        {
            return CheckoutBranch
                ? BranchCreationMode.CheckoutOrphanFromCommit
                : BranchCreationMode.OrphanFromCommit;
        }

        return CheckoutBranch
            ? BranchCreationMode.CheckoutEmptyOrphan
            : BranchCreationMode.EmptyOrphanWithInitialCommit;
    }

    private void NotifyStartPointStateChanged()
    {
        OnPropertyChanged(nameof(CanEditStartPoint));
        OnPropertyChanged(nameof(StartPointError));
        OnPropertyChanged(nameof(HasStartPointError));
        OnPropertyChanged(nameof(CanCreate));
    }

    private void OnRevisionSelectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GitRevisionSelectorViewModel.StartPoint)
            or nameof(GitRevisionSelectorViewModel.CanResolve)
            or nameof(GitRevisionSelectorViewModel.IsLoading)
            or nameof(GitRevisionSelectorViewModel.IsValidating)
            or nameof(GitRevisionSelectorViewModel.IsStartPointEnabled))
        {
            NotifyStartPointStateChanged();
        }
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BranchName))
        {
            OnPropertyChanged(nameof(BranchNameError));
            OnPropertyChanged(nameof(HasBranchNameError));
        }

        OnPropertyChanged(nameof(CanCreate));
    }
}
