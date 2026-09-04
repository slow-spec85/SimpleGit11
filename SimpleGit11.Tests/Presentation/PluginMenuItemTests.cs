using System.Collections.Concurrent;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Presentation.Navigation;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class PluginMenuItemTests
{
    [TestMethod]
    public void Constructor_CapturesIdentityPlacementAndAccessibleState()
    {
        TestContribution contribution = new(new RelayCommand(() => { }))
        {
            Indicator = new(MainMenuIndicatorKind.Success, "Connected to server")
        };
        using PluginMenuItem item = new(contribution, action => action());

        Assert.AreEqual("PluginMenu.test.connection", item.AutomationId);
        Assert.AreEqual(MainMenuPlacement.Footer, item.Placement);
        Assert.AreEqual("Connection — Connected to server", item.State.AccessibleName);
        Assert.AreEqual("Connected to server", item.State.ToolTipText);
        Assert.IsTrue(item.State.IsEnabled);
    }

    [TestMethod]
    [DataRow(MainMenuIndicatorKind.Success, "Connected to server. Select to disconnect.")]
    [DataRow(MainMenuIndicatorKind.None, "Not connected. Select to configure a connection.")]
    [DataRow(MainMenuIndicatorKind.Progress, "Connection settings are being processed")]
    public void State_ToolTipUsesStatusWithoutRepeatingLabel(MainMenuIndicatorKind kind, string status)
    {
        TestContribution contribution = new(new RelayCommand(() => { }))
        {
            Indicator = new(kind, status)
        };
        using PluginMenuItem item = new(contribution, action => action());

        Assert.AreEqual(status, item.State.ToolTipText);
        Assert.AreEqual($"{contribution.Label} — {status}", item.State.AccessibleName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void State_WithoutStatus_KeepsLabelForToolTip(string status)
    {
        TestContribution contribution = new(new RelayCommand(() => { }))
        {
            Indicator = new(MainMenuIndicatorKind.None, status)
        };
        using PluginMenuItem item = new(contribution, action => action());

        Assert.AreEqual(contribution.Label, item.State.ToolTipText);
        Assert.AreEqual(contribution.Label, item.State.AccessibleName);
    }

    [TestMethod]
    public async Task InvokeAsync_DisabledCommand_IsNotExecuted()
    {
        int calls = 0;
        using PluginMenuItem item = new(
            new TestContribution(new RelayCommand(() => calls++, () => false)), action => action());

        await item.InvokeAsync();

        Assert.AreEqual(0, calls);
        Assert.IsFalse(item.State.IsEnabled);
    }

    [TestMethod]
    public async Task InvokeAsync_AsyncCommand_IsAwaitedAndCannotRunTwice()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        AsyncRelayCommand command = new(async () =>
        {
            calls++;
            await completion.Task;
        });
        using PluginMenuItem item = new(new TestContribution(command), action => action());

        Task execution = item.InvokeAsync();
        Assert.IsFalse(execution.IsCompleted);
        Assert.IsFalse(item.State.IsEnabled);
        await item.InvokeAsync();
        Assert.AreEqual(1, calls);

        completion.SetResult();
        await execution;
        Assert.IsTrue(item.State.IsEnabled);
    }

    [TestMethod]
    public async Task InvokeAsync_SynchronousCommand_RunsAndRestoresState()
    {
        int calls = 0;
        using PluginMenuItem item = new(
            new TestContribution(new RelayCommand(() => calls++)), action => action());

        await item.InvokeAsync();

        Assert.AreEqual(1, calls);
        Assert.IsTrue(item.State.IsEnabled);
    }

    [TestMethod]
    public async Task InvokeAsync_AsyncFailure_PropagatesAndRestoresState()
    {
        InvalidOperationException failure = new("Test failure");
        AsyncRelayCommand command = new(() => Task.FromException(failure));
        using PluginMenuItem item = new(new TestContribution(command), action => action());

        Exception actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(item.InvokeAsync);

        Assert.AreSame(failure, actual);
        Assert.IsTrue(item.State.IsEnabled);
    }

    [TestMethod]
    public async Task PropertyChanged_FromBackgroundThread_UsesDispatcher()
    {
        ConcurrentQueue<Action> pending = new();
        TestContribution contribution = new(new RelayCommand(() => { }));
        using PluginMenuItem item = new(contribution, pending.Enqueue);

        await Task.Run(() => contribution.Label = "Updated label");

        Assert.AreEqual("Connection", item.State.Label);
        Assert.IsTrue(pending.TryDequeue(out Action? update));
        update();
        Assert.AreEqual("Updated label", item.State.Label);
        Assert.AreEqual("Updated label", item.State.AccessibleName);
    }

    [TestMethod]
    public void PropertyChanged_IndicatorAndGlyph_UpdateAccessibleState()
    {
        TestContribution contribution = new(new RelayCommand(() => { }));
        using PluginMenuItem item = new(contribution, action => action());

        contribution.Indicator = new(MainMenuIndicatorKind.Warning, "Connection lost");
        contribution.IconGlyph = "\uE839";

        Assert.AreEqual(MainMenuIndicatorKind.Warning, item.State.Indicator.Kind);
        Assert.AreEqual("Connection — Connection lost", item.State.AccessibleName);
        Assert.AreEqual("Connection lost", item.State.ToolTipText);
        Assert.AreEqual("\uE839", item.State.IconGlyph);
        contribution.Indicator = new(MainMenuIndicatorKind.None, "Disconnected");
        Assert.AreEqual("Connection — Disconnected", item.State.AccessibleName);
        Assert.AreEqual("Disconnected", item.State.ToolTipText);
    }

    [TestMethod]
    public void CanExecuteChanged_UpdatesEnabledState()
    {
        bool canExecute = true;
        RelayCommand command = new(() => { }, () => canExecute);
        using PluginMenuItem item = new(new TestContribution(command), action => action());

        canExecute = false;
        command.NotifyCanExecuteChanged();
        Assert.IsFalse(item.State.IsEnabled);

        canExecute = true;
        command.NotifyCanExecuteChanged();
        Assert.IsTrue(item.State.IsEnabled);
    }

    [TestMethod]
    public void CommandReplacement_UnsubscribesOldCommandAndObservesNewCommand()
    {
        Queue<Action> pending = new();
        RelayCommand oldCommand = new(() => { });
        RelayCommand newCommand = new(() => { }, () => false);
        TestContribution contribution = new(oldCommand);
        using PluginMenuItem item = new(contribution, pending.Enqueue);

        contribution.Command = newCommand;
        pending.Dequeue()();
        Assert.IsFalse(item.State.IsEnabled);
        oldCommand.NotifyCanExecuteChanged();
        Assert.IsEmpty(pending);
        newCommand.NotifyCanExecuteChanged();
        Assert.HasCount(1, pending);
    }

    [TestMethod]
    public async Task Dispose_UnsubscribesAndIgnoresQueuedUpdatesAndInvocations()
    {
        Queue<Action> pending = new();
        int calls = 0;
        RelayCommand command = new(() => calls++);
        TestContribution contribution = new(command);
        PluginMenuItem item = new(contribution, pending.Enqueue);
        contribution.Label = "Queued update";
        item.Dispose();
        pending.Dequeue()();

        contribution.Label = "After disposal";
        command.NotifyCanExecuteChanged();
        await item.InvokeAsync();

        Assert.IsEmpty(pending);
        Assert.AreEqual("Connection", item.State.Label);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Constructor_IndicatorWithoutAccessibleDescription_IsRejected()
    {
        TestContribution contribution = new(new RelayCommand(() => { }))
        {
            Indicator = new(MainMenuIndicatorKind.Success, string.Empty)
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => new PluginMenuItem(contribution, action => action()));
    }

    [TestMethod]
    public void Constructor_UnknownPlacement_IsRejected()
    {
        TestContribution contribution = new(new RelayCommand(() => { }))
        {
            Placement = (MainMenuPlacement)99
        };

        Assert.ThrowsExactly<ArgumentException>(() => new PluginMenuItem(contribution, action => action()));
    }

    private sealed class TestContribution(ICommand command) : ObservableObject, IMainMenuContribution
    {
        private string _label = "Connection";
        private string _iconGlyph = string.Empty;
        private MainMenuIndicator _indicator = MainMenuIndicator.None;
        private ICommand _command = command;

        public string Id => "test.connection";

        public MainMenuPlacement Placement { get; init; } = MainMenuPlacement.Footer;

        public string Label { get => _label; set => SetProperty(ref _label, value); }

        public string IconGlyph { get => _iconGlyph; set => SetProperty(ref _iconGlyph, value); }

        public MainMenuIndicator Indicator { get => _indicator; set => SetProperty(ref _indicator, value); }

        public ICommand Command { get => _command; set => SetProperty(ref _command, value); }
    }
}
