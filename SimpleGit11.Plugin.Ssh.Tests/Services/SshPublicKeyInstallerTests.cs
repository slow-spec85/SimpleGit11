using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshPublicKeyInstallerTests
{
    [TestMethod]
    public async Task InstallAsync_FirstLinuxConnection_InstallsKeyUsingPasswordSession()
    {
        FakeCommandExecutor executor = new(
            new SshCommandResult(0, "Linux\n", ""),
            new SshCommandResult(0, "", ""));
        FakePrivateKeyService privateKeyService = new();
        SshConnectionSettings? connectedSettings = null;
        SshPublicKeyInstaller installer = new(
            privateKeyService,
            (settings, _, _) =>
            {
                connectedSettings = settings;
                return Task.FromResult<ISshCommandExecutor>(executor);
            });

        await installer.InstallAsync(Settings(), new SshConnectionMonitor(), CancellationToken.None);

        Assert.IsNotNull(connectedSettings);
        Assert.AreEqual("secret", connectedSettings.Password);
        Assert.IsNull(connectedSettings.PrivateKeyPath);
        Assert.IsNull(connectedSettings.PrivateKeyPassphrase);
        Assert.HasCount(2, executor.Commands);
        Assert.AreEqual("uname -s", executor.Commands[0].Command);
        StringAssert.Contains(executor.Commands[1].Command, "$HOME/.ssh/authorized_keys");
        StringAssert.Contains(executor.Commands[1].Command, "chmod 700");
        StringAssert.Contains(executor.Commands[1].Command, "chmod 600");
        StringAssert.Contains(executor.Commands[1].Command, "grep -Fqx");
        Assert.AreEqual("ssh-ed25519 AQID\n", executor.Commands[1].StandardInput);
        Assert.IsTrue(executor.IsDisposed);
        Assert.IsTrue(privateKeyService.WasRead);
    }

    [TestMethod]
    public async Task InstallAsync_NoPassword_SkipsInstallationForVerifiedKeyConnection()
    {
        bool connectorCalled = false;
        FakePrivateKeyService privateKeyService = new();
        SshPublicKeyInstaller installer = new(
            privateKeyService,
            (_, _, _) =>
            {
                connectorCalled = true;
                throw new InvalidOperationException();
            });

        await installer.InstallAsync(
            Settings() with { Password = null },
            new SshConnectionMonitor(),
            CancellationToken.None);

        Assert.IsFalse(connectorCalled);
        Assert.IsFalse(privateKeyService.WasRead);
    }

    [TestMethod]
    public async Task InstallAsync_UntrustedHost_RequestsVerificationBeforeReadingPrivateKey()
    {
        FakePrivateKeyService privateKeyService = new();
        SshHostKeyVerificationException expected = new(
            "server.example",
            "SHA256:received",
            null);
        SshPublicKeyInstaller installer = new(
            privateKeyService,
            (_, _, _) => Task.FromException<ISshCommandExecutor>(expected));

        SshHostKeyVerificationException actual =
            await Assert.ThrowsExactlyAsync<SshHostKeyVerificationException>(() =>
                installer.InstallAsync(
                    Settings() with { ExpectedHostKey = null },
                    new SshConnectionMonitor(),
                    CancellationToken.None));

        Assert.AreSame(expected, actual);
        Assert.IsFalse(privateKeyService.WasRead);
    }

    [TestMethod]
    public async Task InstallAsync_NonLinuxHost_RejectsAutomaticInstallation()
    {
        FakeCommandExecutor executor = new(new SshCommandResult(0, "Darwin\n", ""));
        SshPublicKeyInstaller installer = new(
            new FakePrivateKeyService(),
            (_, _, _) => Task.FromResult<ISshCommandExecutor>(executor));

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => installer.InstallAsync(
            Settings(),
            new SshConnectionMonitor(),
            CancellationToken.None));

        Assert.HasCount(1, executor.Commands);
        Assert.IsTrue(executor.IsDisposed);
    }

    [TestMethod]
    public async Task InstallAsync_RemoteWriteFails_ReportsServerError()
    {
        FakeCommandExecutor executor = new(
            new SshCommandResult(0, "Linux\n", ""),
            new SshCommandResult(1, "", "Permission denied"));
        SshPublicKeyInstaller installer = new(
            new FakePrivateKeyService(),
            (_, _, _) => Task.FromResult<ISshCommandExecutor>(executor));

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => installer.InstallAsync(
                Settings(),
                new SshConnectionMonitor(),
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "Permission denied");
        Assert.IsTrue(executor.IsDisposed);
    }

    private static SshConnectionSettings Settings() => new(
        "server.example",
        22,
        "git",
        "secret",
        @"C:\keys\id_ed25519",
        "key-secret",
        "SHA256:trusted");

    private sealed class FakeCommandExecutor(params SshCommandResult[] results)
        : ISshCommandExecutor
    {
        private readonly Queue<SshCommandResult> _results = new(results);

        public List<(string Command, string? StandardInput)> Commands { get; } = [];
        public bool IsDisposed { get; private set; }

        public Task<SshCommandResult> ExecuteAsync(
            string commandText,
            string? standardInput = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add((commandText, standardInput));
            return Task.FromResult(_results.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePrivateKeyService : ISshPrivateKeyService
    {
        public bool WasRead { get; private set; }

        public Task GenerateAsync(
            string path,
            string? passphrase,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RequiresPassphraseAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> CanOpenAsync(
            string path,
            string passphrase,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetAuthorizedKeyAsync(
            string path,
            string? passphrase,
            CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return Task.FromResult("ssh-ed25519 AQID");
        }
    }
}
