using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Presentation.Services;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class PluginOperationFeedbackTests
{
    [TestMethod]
    public void Feedback_ForwardsProgressCancellationAndErrorToWindowMessages()
    {
        StrongReferenceMessenger messenger = new();
        List<AppOperationMessage> operations = [];
        List<AppNotificationMessage> notifications = [];
        List<ClearAppNotificationMessage> clears = [];
        messenger.Register<AppOperationMessage>(this, (_, message) => operations.Add(message));
        messenger.Register<AppNotificationMessage>(this, (_, message) => notifications.Add(message));
        messenger.Register<ClearAppNotificationMessage>(this, (_, message) => clears.Add(message));
        PluginOperationFeedback feedback = new(messenger);
        object source = new();
        bool cancelled = false;

        feedback.ClearError(source);
        feedback.Start(source, "Connecting", () => cancelled = true);
        operations.Single().CancelCommand!.Execute(null);
        feedback.Stop(source);
        feedback.ShowError(source, "Failed", "Connection refused");

        Assert.AreSame(source, clears.Single().Source);
        Assert.IsTrue(cancelled);
        Assert.IsTrue(operations[0].IsRunning);
        Assert.IsFalse(operations[1].IsRunning);
        Assert.AreSame(source, operations[0].Source);
        Assert.AreEqual(AppNotificationSeverity.Error, notifications.Single().Severity);
        Assert.AreEqual("Connection refused", notifications.Single().Details);
    }
}
