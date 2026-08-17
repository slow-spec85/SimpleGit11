namespace SimpleGit11.Models;

public sealed class TextInputDialogRequest
{
    public TextInputDialogRequest(
        string title,
        string textBoxHeader,
        string initialValue,
        string primaryButtonText,
        string closeButtonText,
        string placeholderText = "",
        bool isMultiline = false,
        bool allowEmpty = false)
    {
        Title = title;
        TextBoxHeader = textBoxHeader;
        InitialValue = initialValue;
        PrimaryButtonText = primaryButtonText;
        CloseButtonText = closeButtonText;
        PlaceholderText = placeholderText;
        IsMultiline = isMultiline;
        AllowEmpty = allowEmpty;
    }

    public string Title { get; }

    public string TextBoxHeader { get; }

    public string InitialValue { get; }

    public string PrimaryButtonText { get; }

    public string CloseButtonText { get; }

    public string PlaceholderText { get; }

    public bool IsMultiline { get; }

    public bool AllowEmpty { get; }
}
