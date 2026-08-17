using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using System.Linq;
using System.Windows.Input;

namespace SimpleGit11.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

public abstract class ValidatableViewModelBase : ObservableValidator
{
    protected string GetFirstValidationError(string propertyName)
    {
        return GetErrors(propertyName)
            .Select(static error => error.ErrorMessage)
            .FirstOrDefault(static message => !string.IsNullOrWhiteSpace(message))
            ?? "";
    }
}

public abstract class AppNotificationViewModelBase : ViewModelBase
{
    private readonly IMessenger _messenger;

    protected AppNotificationViewModelBase(IMessenger messenger)
    {
        _messenger = messenger;
    }

    protected void ClearNotification()
    {
        _messenger.Send(new ClearAppNotificationMessage(this));
    }

    protected void ShowNotification(
        AppNotificationSeverity severity,
        string message,
        string? details = null,
        ICommand? actionCommand = null,
        string? actionText = null)
    {
        _messenger.Send(new AppNotificationMessage(
            this,
            severity,
            message,
            details?.Trim() ?? "",
            actionCommand,
            actionText));
    }

    protected void PublishOperationState(
        bool isRunning,
        string? message = null,
        ICommand? cancelCommand = null)
    {
        _messenger.Send(new AppOperationMessage(
            this,
            isRunning,
            message?.Trim() ?? "",
            cancelCommand));
    }
}

public abstract partial class LocalNotificationViewModelBase : ViewModelBase
{
    protected LocalNotificationViewModelBase()
    {
        NotificationMessage = "";
        NotificationDetails = "";
    }

    [ObservableProperty]
    public partial bool IsNotificationOpen { get; set; }

    [ObservableProperty]
    public partial string NotificationMessage { get; private set; }

    [ObservableProperty]
    public partial string NotificationDetails { get; private set; }

    [ObservableProperty]
    public partial InfoBarSeverity NotificationSeverity { get; private set; }

    protected void ClearNotification()
    {
        IsNotificationOpen = false;
        NotificationMessage = "";
        NotificationDetails = "";
    }

    protected void ShowNotification(InfoBarSeverity severity, string message)
    {
        ShowNotification(severity, message, "");
    }

    protected void ShowNotification(InfoBarSeverity severity, string message, string? details)
    {
        NotificationSeverity = severity;
        NotificationMessage = message;
        NotificationDetails = details?.Trim() ?? "";
        IsNotificationOpen = true;
    }
}
