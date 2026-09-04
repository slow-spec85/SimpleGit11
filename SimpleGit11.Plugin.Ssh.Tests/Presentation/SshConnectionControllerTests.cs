using SimpleGit11.Tests.Presentation;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Plugin.Ssh.Models;
using SimpleGit11.Plugin.Ssh.Presentation;
using SimpleGit11.Services.Execution;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Presentation;

[TestClass]
public sealed class SshConnectionControllerTests
{
    private readonly ConnectionTestContexts _contexts = new();
    private readonly ConnectionTestProfiles _profiles = new();
    private readonly ConnectionTestDialogs _dialogs = new();
    private readonly ConnectionTestLocalization _localization = new();

    private SshConnectionController CreateController() => new(_contexts, _profiles, _dialogs, _localization);

    private static SshConnectionDialogResult Connection(bool remember = true) => new(
        SshConnectionDialogAction.Connect, "profile", "server", 2222, "user",
        "password", "key-file", "passphrase", "trusted-key", remember);

    [TestMethod]
    public async Task ToggleAsync_Cancel_KeepsCurrentContextAndProfiles()
    {
        Guid previous = _contexts.Current.Id;
        _dialogs.Results.Enqueue(null);

        await CreateController().ToggleAsync();

        Assert.AreEqual(previous, _contexts.Current.Id);
        Assert.IsEmpty(_contexts.Requests);
        Assert.IsEmpty(_profiles.Profiles);
    }

    [TestMethod]
    public async Task ToggleAsync_Connect_PassesSecretsSeparatelyAndSavesProfile()
    {
        _dialogs.Results.Enqueue(Connection());

        await CreateController().ToggleAsync();

        Assert.IsFalse(_contexts.Current.IsLocal);
        ExecutionConnectionRequest request = _contexts.Requests.Single();
        Assert.AreEqual("profile", request.ProfileId);
        Assert.AreEqual("server", request.Settings[SshConnectionRequestKeys.Host]);
        Assert.AreEqual("2222", request.Settings[SshConnectionRequestKeys.Port]);
        Assert.AreEqual("user", request.Settings[SshConnectionRequestKeys.Username]);
        Assert.AreEqual("key-file", request.Settings[SshConnectionRequestKeys.PrivateKeyPath]);
        Assert.AreEqual("trusted-key", request.Settings[SshConnectionRequestKeys.ExpectedHostKey]);
        Assert.IsFalse(request.Settings.ContainsKey(SshConnectionRequestKeys.Password));
        Assert.IsFalse(request.Settings.ContainsKey(SshConnectionRequestKeys.PrivateKeyPassphrase));
        Assert.AreEqual("password", request.Secrets![SshConnectionRequestKeys.Password]);
        Assert.AreEqual("passphrase", request.Secrets[SshConnectionRequestKeys.PrivateKeyPassphrase]);
        SshConnectionProfile profile = _profiles.Profiles.Single();
        Assert.AreEqual("profile", profile.Id);
        Assert.AreEqual("trusted-key", profile.ExpectedHostKey);
        Assert.AreEqual("key-file", profile.PrivateKeyPath);
    }

    [TestMethod]
    public async Task ToggleAsync_TransientConnection_DoesNotSaveProfileOrProfileId()
    {
        _dialogs.Results.Enqueue(Connection(false) with { Password = null, PrivateKeyPassphrase = null, PrivateKeyPath = null, ExpectedHostKey = null });

        await CreateController().ToggleAsync();

        ExecutionConnectionRequest request = _contexts.Requests.Single();
        Assert.IsNull(request.ProfileId);
        Assert.IsEmpty(request.Secrets!);
        Assert.HasCount(3, request.Settings);
        Assert.IsEmpty(_profiles.Profiles);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ToggleAsync_HostKeyChallenge_RetriesOnlyAfterConfirmation(bool trust)
    {
        _dialogs.Results.Enqueue(Connection());
        _dialogs.Confirmations.Enqueue(trust);
        _contexts.Connect = request => request.Settings[SshConnectionRequestKeys.ExpectedHostKey] == "new-key"
            ? Task.CompletedTask
            : Task.FromException(new SshHostKeyVerificationException("server", "new-key", "trusted-key"));

        await CreateController().ToggleAsync();

        Assert.AreEqual(trust ? 2 : 1, _contexts.Requests.Count);
        Assert.AreEqual(!trust, _contexts.Current.IsLocal);
        Assert.HasCount(1, _dialogs.Prompts);
        if (trust)
        {
            Assert.AreEqual("new-key", _profiles.Profiles.Single().ExpectedHostKey);
        }
        else
        {
            Assert.IsEmpty(_profiles.Profiles);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ToggleAsync_DeleteProfile_RequiresConfirmationAndReopensDialog(bool confirm)
    {
        _profiles.Profiles.Add(new("profile", "server", 2222, "user", null, "key", DateTimeOffset.UtcNow));
        _dialogs.Results.Enqueue(Connection() with { Action = SshConnectionDialogAction.DeleteProfile });
        _dialogs.Results.Enqueue(null);
        _dialogs.Confirmations.Enqueue(confirm);

        await CreateController().ToggleAsync();

        Assert.HasCount(confirm ? 0 : 1, _profiles.Profiles);
        Assert.HasCount(2, _dialogs.Shown);
        Assert.AreEqual("profile", _dialogs.Shown[0].Selected);
        Assert.HasCount(confirm ? 0 : 1, _dialogs.Shown[1].Profiles);
        Assert.IsEmpty(_contexts.Requests);
    }

    [TestMethod]
    public async Task ToggleAsync_ConnectionFailure_PropagatesWithoutSavingAndClearsBusy()
    {
        Guid previous = _contexts.Current.Id;
        _dialogs.Results.Enqueue(Connection());
        _contexts.Connect = _ => Task.FromException(new IOException("Connection failed"));
        SshConnectionController controller = CreateController();

        await Assert.ThrowsExactlyAsync<IOException>(controller.ToggleAsync);

        Assert.IsFalse(controller.IsBusy);
        Assert.AreEqual(previous, _contexts.Current.Id);
        Assert.IsEmpty(_profiles.Profiles);
    }

    [TestMethod]
    public async Task ToggleAsync_DuplicateRequest_IsIgnoredWhileDialogIsOpen()
    {
        TaskCompletionSource<SshConnectionDialogResult?> completion = new();
        _dialogs.Show = () => completion.Task;
        SshConnectionController controller = CreateController();
        using SshMainMenuContribution menu = new(controller, _contexts, _localization);

        Task first = ((IAsyncRelayCommand)menu.Command).ExecuteAsync(null);
        Assert.IsTrue(controller.IsBusy);
        Assert.IsFalse(menu.Command.CanExecute(null));
        Assert.AreEqual(MainMenuIndicatorKind.Progress, menu.Indicator.Kind);
        await controller.ToggleAsync();
        Assert.HasCount(1, _dialogs.Shown);

        completion.SetResult(null);
        await first;
        Assert.IsFalse(controller.IsBusy);
        Assert.IsTrue(menu.Command.CanExecute(null));
        Assert.AreEqual(MainMenuIndicatorKind.None, menu.Indicator.Kind);
    }

    [TestMethod]
    public async Task ToggleAsync_ContextChangedDuringDialog_DoesNotReplaceNewConnection()
    {
        TaskCompletionSource<SshConnectionDialogResult?> completion = new();
        _dialogs.Show = () => completion.Task;
        Task operation = CreateController().ToggleAsync();
        _contexts.Switch(false, "other.provider");
        Guid current = _contexts.Current.Id;

        completion.SetResult(Connection());
        await operation;

        Assert.AreEqual(current, _contexts.Current.Id);
        Assert.IsEmpty(_contexts.Requests);
        Assert.IsEmpty(_profiles.Profiles);
    }

    [TestMethod]
    public async Task Menu_ConnectionAndDisconnect_KeepLabelAndUpdateIndicator()
    {
        _dialogs.Results.Enqueue(Connection());
        _dialogs.Confirmations.Enqueue(true);
        using SshMainMenuContribution menu = new(CreateController(), _contexts, _localization);
        string label = menu.Label;
        int notifications = 0;
        menu.PropertyChanged += (_, _) => notifications++;

        await ((IAsyncRelayCommand)menu.Command).ExecuteAsync(null);

        Assert.AreEqual(label, menu.Label);
        Assert.AreEqual(MainMenuIndicatorKind.Success, menu.Indicator.Kind);
        StringAssert.Contains(menu.Indicator.AccessibleText, "test-server");
        Assert.IsGreaterThan(0, notifications);

        await ((IAsyncRelayCommand)menu.Command).ExecuteAsync(null);

        Assert.AreEqual(label, menu.Label);
        Assert.AreEqual(MainMenuIndicatorKind.None, menu.Indicator.Kind);
        Assert.AreEqual(1, _contexts.UseLocalCalls);
        Assert.HasCount(1, _dialogs.Shown);
        Assert.HasCount(1, _dialogs.Prompts);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ToggleAsync_Disconnect_RequiresConfirmation(bool confirm)
    {
        _contexts.Switch(false, SshPlugin.ProviderId);
        Guid contextId = _contexts.Current.Id;
        string machineName = _contexts.Current.DisplayMachineName;
        _dialogs.Confirmations.Enqueue(confirm);

        await CreateController().ToggleAsync();

        Assert.AreEqual(confirm, _contexts.Current.IsLocal);
        Assert.AreEqual(confirm ? 1 : 0, _contexts.UseLocalCalls);
        if (!confirm) Assert.AreEqual(contextId, _contexts.Current.Id);
        Assert.HasCount(1, _dialogs.Prompts);
        Assert.AreEqual(_localization.GetString("SshDisconnectDialogTitle"), _dialogs.Prompts[0].Title);
        Assert.AreEqual(
            string.Format(_localization.GetString("SshDisconnectDialogMessage"), machineName),
            _dialogs.Prompts[0].Message);
        Assert.AreEqual(_localization.GetString("SshDisconnectConfirmButton"), _dialogs.Prompts[0].PrimaryButtonText);
        Assert.IsEmpty(_dialogs.Shown);
        Assert.IsEmpty(_contexts.Requests);
    }

    [TestMethod]
    [DataRow(false, "ssh")]
    [DataRow(false, "other.provider")]
    [DataRow(true, "local")]
    public async Task ToggleAsync_ContextChangesDuringDisconnectConfirmation_KeepsNewContext(bool local, string providerId)
    {
        _contexts.Switch(false, SshPlugin.ProviderId);
        TaskCompletionSource<bool> confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.Confirm = () => confirmation.Task;
        Task operation = CreateController().ToggleAsync();
        _contexts.Switch(local, providerId == "ssh" ? SshPlugin.ProviderId : providerId);
        Guid replacementId = _contexts.Current.Id;

        confirmation.SetResult(true);
        await operation;

        Assert.AreEqual(replacementId, _contexts.Current.Id);
        Assert.AreEqual(0, _contexts.UseLocalCalls);
    }

    [TestMethod]
    public async Task ToggleAsync_DisconnectConfirmation_DuplicateIgnoredAndCancelRestoresMenu()
    {
        _contexts.Switch(false, SshPlugin.ProviderId);
        TaskCompletionSource<bool> confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.Confirm = () => confirmation.Task;
        SshConnectionController controller = CreateController();
        using SshMainMenuContribution menu = new(controller, _contexts, _localization);

        Task operation = ((IAsyncRelayCommand)menu.Command).ExecuteAsync(null);
        Assert.IsTrue(controller.IsBusy);
        Assert.IsFalse(menu.Command.CanExecute(null));
        await controller.ToggleAsync();
        Assert.HasCount(1, _dialogs.Prompts);
        Assert.AreEqual(0, _contexts.UseLocalCalls);

        confirmation.SetResult(false);
        await operation;

        Assert.IsFalse(controller.IsBusy);
        Assert.IsTrue(menu.Command.CanExecute(null));
        Assert.AreEqual(MainMenuIndicatorKind.Success, menu.Indicator.Kind);
        Assert.AreEqual(0, _contexts.UseLocalCalls);
    }

    [TestMethod]
    public async Task ToggleAsync_DisconnectConfirmationFailure_KeepsConnectionAndClearsBusy()
    {
        _contexts.Switch(false, SshPlugin.ProviderId);
        Guid contextId = _contexts.Current.Id;
        _dialogs.Confirm = () => Task.FromException<bool>(new InvalidOperationException("Dialog failed"));
        SshConnectionController controller = CreateController();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(controller.ToggleAsync);

        Assert.IsFalse(controller.IsBusy);
        Assert.AreEqual(contextId, _contexts.Current.Id);
        Assert.AreEqual(0, _contexts.UseLocalCalls);
    }

    [TestMethod]
    public async Task Menu_OtherProvider_DoesNotDisconnectIt()
    {
        SshConnectionController controller = CreateController();
        using SshMainMenuContribution menu = new(controller, _contexts, _localization);
        _contexts.Switch(false, "other.provider");

        Assert.IsFalse(menu.Command.CanExecute(null));
        Assert.AreEqual(MainMenuIndicatorKind.None, menu.Indicator.Kind);
        await controller.ToggleAsync();
        Assert.AreEqual(0, _contexts.UseLocalCalls);
        Assert.IsEmpty(_dialogs.Shown);
        Assert.IsEmpty(_dialogs.Prompts);
    }

    [TestMethod]
    public void Menu_Dispose_UnsubscribesContextChangesAndDisablesCommand()
    {
        SshMainMenuContribution menu = new(CreateController(), _contexts, _localization);
        int changes = 0;
        menu.PropertyChanged += (_, _) => changes++;
        menu.Dispose();

        _contexts.Switch(false);

        Assert.AreEqual(0, changes);
        Assert.IsFalse(menu.Command.CanExecute(null));
    }
}
