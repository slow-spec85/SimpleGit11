using SimpleGit11.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SimpleGit11.ViewModels;

public sealed partial class TagCreateDialogViewModel : ValidatableViewModelBase
{
    private readonly DialogValidationMessages _validationMessages;

    public TagCreateDialogViewModel(
        IReadOnlyList<GitCommit> commits,
        DialogValidationMessages validationMessages)
    {
        _validationMessages = validationMessages;
        ErrorsChanged += OnErrorsChanged;

        foreach (GitCommit commit in commits)
        {
            Commits.Add(commit);
        }

        TagName = "";
        Message = "";
        SelectedCommit = Commits.FirstOrDefault();
        ValidateAllProperties();
    }

    public ObservableCollection<GitCommit> Commits { get; } = [];

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [CustomValidation(typeof(TagCreateDialogViewModel), nameof(ValidateTagName))]
    public partial string TagName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial bool IsAnnotated { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [CustomValidation(typeof(TagCreateDialogViewModel), nameof(ValidateMessage))]
    public partial string Message { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [CustomValidation(typeof(TagCreateDialogViewModel), nameof(ValidateSelectedCommit))]
    public partial GitCommit? SelectedCommit { get; set; }

    public string TagNameError => GetFirstValidationError(nameof(TagName));

    public bool HasTagNameError => !string.IsNullOrEmpty(TagNameError);

    public string MessageError => GetFirstValidationError(nameof(Message));

    public bool HasMessageError => !string.IsNullOrEmpty(MessageError);

    public string SelectedCommitError => GetFirstValidationError(nameof(SelectedCommit));

    public bool HasSelectedCommitError => !string.IsNullOrEmpty(SelectedCommitError);

    public bool CanCreate => !HasErrors;

    public static ValidationResult? ValidateTagName(
        string? tagName,
        ValidationContext validationContext)
    {
        TagCreateDialogViewModel viewModel =
            (TagCreateDialogViewModel)validationContext.ObjectInstance;

        return string.IsNullOrWhiteSpace(tagName)
            ? new ValidationResult(viewModel._validationMessages.RequiredField)
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateMessage(
        string? message,
        ValidationContext validationContext)
    {
        TagCreateDialogViewModel viewModel =
            (TagCreateDialogViewModel)validationContext.ObjectInstance;

        return viewModel.IsAnnotated && string.IsNullOrWhiteSpace(message)
            ? new ValidationResult(viewModel._validationMessages.RequiredField)
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateSelectedCommit(
        GitCommit? selectedCommit,
        ValidationContext validationContext)
    {
        TagCreateDialogViewModel viewModel =
            (TagCreateDialogViewModel)validationContext.ObjectInstance;

        return selectedCommit is null
            ? new ValidationResult(viewModel._validationMessages.SelectionRequired)
            : ValidationResult.Success;
    }

    partial void OnIsAnnotatedChanged(bool value)
    {
        ValidateProperty(Message, nameof(Message));
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TagName):
                OnPropertyChanged(nameof(TagNameError));
                OnPropertyChanged(nameof(HasTagNameError));
                break;
            case nameof(Message):
                OnPropertyChanged(nameof(MessageError));
                OnPropertyChanged(nameof(HasMessageError));
                break;
            case nameof(SelectedCommit):
                OnPropertyChanged(nameof(SelectedCommitError));
                OnPropertyChanged(nameof(HasSelectedCommitError));
                break;
        }

        OnPropertyChanged(nameof(CanCreate));
    }
}
