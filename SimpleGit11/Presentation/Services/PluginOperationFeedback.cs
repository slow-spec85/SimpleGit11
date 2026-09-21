using System;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Messages;

namespace SimpleGit11.Presentation.Services;

public sealed class PluginOperationFeedback(IMessenger messenger) : IPluginOperationFeedback
{
    public void Start(object source, string message, Action cancel) =>
        messenger.Send(new AppOperationMessage(source, true, message, new RelayCommand(cancel)));

    public void Stop(object source) =>
        messenger.Send(new AppOperationMessage(source, false, ""));

    public void ShowError(object source, string message, string details) =>
        messenger.Send(new AppNotificationMessage(source, AppNotificationSeverity.Error, message, details));

    public void ClearError(object source) =>
        messenger.Send(new ClearAppNotificationMessage(source));
}
