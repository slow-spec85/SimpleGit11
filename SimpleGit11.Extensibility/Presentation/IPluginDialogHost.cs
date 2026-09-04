using Microsoft.UI.Xaml.Controls;

namespace SimpleGit11.Extensibility.Presentation;

/// <summary>Shows plugin-owned dialogs on the registered window with its current theme.</summary>
public interface IPluginDialogHost
{
    Task<ContentDialogResult> ShowAsync(ContentDialog dialog);
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText);
}
