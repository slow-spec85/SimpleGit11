using SimpleGit11.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SimpleGit11.ViewModels;

public sealed partial class TextInputDialogViewModel : ValidatableViewModelBase
{
    private readonly DialogValidationMessages _validationMessages;

    public TextInputDialogViewModel(
        TextInputDialogRequest request,
        DialogValidationMessages validationMessages)
    {
        _validationMessages = validationMessages;
        ErrorsChanged += OnErrorsChanged;
        Title = request.Title;
        TextBoxHeader = request.TextBoxHeader;
        PrimaryButtonText = request.PrimaryButtonText;
        CloseButtonText = request.CloseButtonText;
        PlaceholderText = request.PlaceholderText;
        IsMultiline = request.IsMultiline;
        AllowEmpty = request.AllowEmpty;
        Text = request.InitialValue;
        ValidateAllProperties();
    }

    public string Title { get; }

    public string TextBoxHeader { get; }

    public string PrimaryButtonText { get; }

    public string CloseButtonText { get; }

    public string PlaceholderText { get; }

    public bool IsMultiline { get; }

    public bool AllowEmpty { get; }

    public bool AcceptsReturn => IsMultiline;

    public TextWrapping TextWrapping => IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public ScrollBarVisibility VerticalScrollBarVisibility =>
        IsMultiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

    public double TextBoxHeight => IsMultiline ? 260 : double.NaN;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [CustomValidation(typeof(TextInputDialogViewModel), nameof(ValidateText))]
    public partial string Text { get; set; }

    public string TextError => GetFirstValidationError(nameof(Text));

    public bool HasTextError => !string.IsNullOrEmpty(TextError);

    public bool CanSubmit => !HasErrors;

    public static ValidationResult? ValidateText(
        string? text,
        ValidationContext validationContext)
    {
        TextInputDialogViewModel viewModel =
            (TextInputDialogViewModel)validationContext.ObjectInstance;

        return !viewModel.AllowEmpty && string.IsNullOrWhiteSpace(text)
            ? new ValidationResult(viewModel._validationMessages.RequiredField)
            : ValidationResult.Success;
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Text))
        {
            OnPropertyChanged(nameof(TextError));
            OnPropertyChanged(nameof(HasTextError));
        }

        OnPropertyChanged(nameof(CanSubmit));
    }
}
