using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using AppExecutionContext = SimpleGit11.Services.Execution.ExecutionContext;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class OpenSshServiceTests
{
    private const string Ed25519Key = "AAAAC3NzaC1lZDI1NTE5AAAAIEjHsYQ5QhGVBp51EWFcIGu0pL7L+v4mXTXoD03QOZ8s";

    [TestMethod]
    [DataRow("git@example.com:team/repository.git", "example.com", 22, "git")]
    [DataRow("ssh://deploy@example.com:2222/team/repository.git", "example.com", 2222, "deploy")]
    public void TryParseEndpoint_ParsesSupportedSshUrls(string url, string host, int port, string user)
    {
        bool parsed = OpenSshService.TryParseEndpoint(url, out OpenSshService.HostEndpoint endpoint);

        Assert.IsTrue(parsed);
        Assert.AreEqual(host, endpoint.Host);
        Assert.AreEqual(port, endpoint.Port);
        Assert.AreEqual(user, endpoint.User);
    }

    [TestMethod]
    [DataRow("https://example.com/team/repository.git")]
    [DataRow("D:\\repositories\\local")]
    [DataRow("../local-repository")]
    public void TryParseEndpoint_RejectsNonSshUrls(string url)
    {
        Assert.IsFalse(OpenSshService.TryParseEndpoint(url, out _));
    }

    [TestMethod]
    public async Task EnsureTrustedAsync_ConfirmsFingerprintAndAppendsScannedKey()
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.GetPath(".ssh/known_hosts");
        RecordingConfirmationService confirmationService = new(confirmed: true);
        OpenSshService service = CreateService(confirmationService, knownHostsPath);

        await service.EnsureTrustedAsync("git@example.com:team/repository.git");

        Assert.IsNotNull(confirmationService.Confirmation);
        Assert.AreEqual("example.com", confirmationService.Confirmation.Host);
        StringAssert.StartsWith(confirmationService.Confirmation.Fingerprints[0], "ssh-ed25519 SHA256:");
        string content = await File.ReadAllTextAsync(knownHostsPath);
        StringAssert.Contains(content, $"example.com ssh-ed25519 {Ed25519Key}");
    }

    [TestMethod]
    public async Task EnsureTrustedAsync_DoesNotWriteKeyWhenConfirmationIsDeclined()
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.GetPath(".ssh/known_hosts");
        OpenSshService service = CreateService(
            new RecordingConfirmationService(confirmed: false),
            knownHostsPath);

        await service.EnsureTrustedAsync("ssh://git@example.com/team/repository.git");

        Assert.IsFalse(File.Exists(knownHostsPath));
    }

    [TestMethod]
    public async Task EnsureTrustedAsync_DoesNotScanOrPromptForKnownHost()
    {
        using TemporaryDirectory directory = new();
        string knownHostsPath = directory.CreateFile(
            ".ssh/known_hosts",
            $"example.com ssh-ed25519 {Ed25519Key}{Environment.NewLine}");
        RecordingConfirmationService confirmationService = new(confirmed: true);
        bool processStarted = false;
        OpenSshService service = new(
            confirmationService,
            knownHostsPath,
            (_, _, _) =>
            {
                processStarted = true;
                return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
            });

        await service.EnsureTrustedAsync("git@example.com:team/repository.git");

        Assert.IsFalse(processStarted);
        Assert.IsNull(confirmationService.Confirmation);
    }

    [TestMethod]
    public async Task EnsureTrustedAsync_RemoteExecution_UpdatesRemoteKnownHosts()
    {
        RecordingConfirmationService confirmationService = new(confirmed: true);
        RecordingRemoteHostKeyStore remoteStore = new();
        OpenSshService service = new(
            confirmationService,
            "unused-local-known-hosts",
            (_, _, _) => throw new AssertFailedException("The local ssh-keyscan must not be used."),
            new RemoteExecutionContextService(remoteStore));

        await service.EnsureTrustedAsync("git@example.com:team/repository.git");

        Assert.AreEqual("example.com", remoteStore.ScannedHost);
        Assert.AreEqual(22, remoteStore.ScannedPort);
        Assert.HasCount(1, remoteStore.AppendedKeyLines);
        Assert.IsNotNull(confirmationService.Confirmation);
    }

    [TestMethod]
    public async Task EnsureTrustedAsync_RemoteWithoutHostKeyStore_DoesNotUseLocalKnownHosts()
    {
        using TemporaryDirectory directory = new();
        bool processStarted = false;
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            directory.GetPath(".ssh/known_hosts"),
            (_, _, _) =>
            {
                processStarted = true;
                return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
            },
            new TestExecutionContextService(new InMemoryRepositoryFileSystem()));

        await service.EnsureTrustedAsync("git@example.com:team/repository.git");

        Assert.IsFalse(processStarted);
        Assert.IsFalse(File.Exists(directory.GetPath(".ssh/known_hosts")));
    }

    [TestMethod]
    public async Task GetIdentitiesAsync_IgnoresTokenizedIdentityFileAndKeepsScanningKeys()
    {
        using TemporaryDirectory directory = new();
        directory.CreateFile("config", "Host *\n    IdentityFile ~/.ssh/id_%h_%r\n");
        string privateKeyPath = directory.CreateFile("id_ed25519_gitlab_com", "private");
        directory.CreateFile(
            "id_ed25519_gitlab_com.pub",
            $"ssh-ed25519 {Ed25519Key} gitlab.com on workstation\n");
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            directory.GetPath("known_hosts"),
            (_, _, _) => throw new AssertFailedException("No OpenSSH process should be started."),
            sshDirectoryPath: directory.Path);

        IReadOnlyList<SshIdentity> identities = await service.GetIdentitiesAsync();

        SshIdentity identity = Assert.ContainsSingle(identities);
        Assert.AreEqual(privateKeyPath, identity.PrivateKeyPath);
        Assert.IsFalse(identity.IsConfigured);
    }

    [TestMethod]
    public async Task CreateIdentityAsync_LocalExecution_UsesHostSpecificIdentityFile()
    {
        using TemporaryDirectory directory = new();
        string privateKeyPath = directory.GetPath("id_ed25519_example_com");
        bool keyWasAddedToAgent = false;
        bool keyWasEncrypted = false;
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            directory.GetPath("known_hosts"),
            (fileName, arguments, _) =>
            {
                if (fileName == "ssh")
                {
                    return Task.FromResult(new OpenSshService.ProcessResult(
                        0,
                        $"identityfile {privateKeyPath}\n",
                        ""));
                }

                if (fileName == "ssh-add")
                {
                    CollectionAssert.AreEqual(new[] { privateKeyPath }, arguments.ToArray());
                    keyWasAddedToAgent = true;
                    return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
                }

                Assert.AreEqual("ssh-keygen", fileName);
                CollectionAssert.Contains(arguments.ToList(), privateKeyPath);
                if (arguments.Contains("-p"))
                {
                    CollectionAssert.Contains(arguments.ToList(), "secret-passphrase");
                    keyWasEncrypted = true;
                    return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
                }

                CollectionAssert.DoesNotContain(arguments.ToList(), "SimpleGit11");
                StringAssert.Contains(string.Join(' ', arguments), "example.com on ");
                File.WriteAllText(privateKeyPath, "private");
                File.WriteAllText(
                    privateKeyPath + ".pub",
                    $"ssh-ed25519 {Ed25519Key} test@example.com\n");
                return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
            },
            sshDirectoryPath: directory.Path);

        SshIdentity identity = await service.CreateIdentityAsync(
            "git@example.com:team/repository.git",
            "secret-passphrase");

        Assert.AreEqual(privateKeyPath, identity.PrivateKeyPath);
        Assert.IsTrue(File.Exists(privateKeyPath));
        Assert.IsTrue(identity.IsConfigured);
        Assert.IsTrue(keyWasAddedToAgent);
        Assert.IsTrue(keyWasEncrypted);
        StringAssert.Contains(File.ReadAllText(directory.GetPath("config")), "Include config.d/simplegit11.conf");
        string managedConfig = File.ReadAllText(directory.GetPath("config.d/simplegit11.conf"));
        StringAssert.Contains(managedConfig, "Host example.com");
        StringAssert.Contains(managedConfig, privateKeyPath.Replace('\\', '/'));
    }

    [TestMethod]
    public async Task CreateIdentityAsync_AgentFailureStillEncryptsPrivateKeyBeforeReportingError()
    {
        using TemporaryDirectory directory = new();
        string privateKeyPath = directory.GetPath("id_ed25519_example_com");
        bool keyWasEncrypted = false;
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            directory.GetPath("known_hosts"),
            (fileName, arguments, _) =>
            {
                if (fileName == "ssh-keygen" && !arguments.Contains("-p"))
                {
                    File.WriteAllText(privateKeyPath, "private");
                    File.WriteAllText(
                        privateKeyPath + ".pub",
                        $"ssh-ed25519 {Ed25519Key} test@example.com\n");
                    return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
                }

                if (fileName == "ssh-keygen")
                {
                    keyWasEncrypted = true;
                    return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
                }

                return Task.FromResult(fileName == "powershell.exe"
                    ? new OpenSshService.ProcessResult(0, "", "")
                    : new OpenSshService.ProcessResult(2, "", "agent unavailable"));
            },
            sshDirectoryPath: directory.Path);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateIdentityAsync("git@example.com:team/repository.git", "secret-passphrase"));

        Assert.IsTrue(keyWasEncrypted);
        StringAssert.Contains(exception.Message, "agent unavailable");
        Assert.IsFalse(File.Exists(directory.GetPath("config.d/simplegit11.conf")));
    }

    [TestMethod]
    public async Task CreateIdentityAsync_EmptyPassphraseCreatesUnencryptedConfiguredKeyWithoutAgent()
    {
        using TemporaryDirectory directory = new();
        string privateKeyPath = directory.GetPath("id_ed25519_example_com");
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            directory.GetPath("known_hosts"),
            (fileName, arguments, _) =>
            {
                Assert.AreEqual("ssh-keygen", fileName);
                CollectionAssert.DoesNotContain(arguments.ToList(), "-p");
                int passphraseIndex = arguments.ToList().IndexOf("-N");
                Assert.IsTrue(passphraseIndex >= 0);
                Assert.AreEqual("", arguments[passphraseIndex + 1]);
                File.WriteAllText(privateKeyPath, "private");
                File.WriteAllText(
                    privateKeyPath + ".pub",
                    $"ssh-ed25519 {Ed25519Key} test@example.com\n");
                return Task.FromResult(new OpenSshService.ProcessResult(0, "", ""));
            },
            sshDirectoryPath: directory.Path);

        SshIdentity identity = await service.CreateIdentityAsync(
            "git@example.com:team/repository.git",
            "");

        Assert.IsTrue(identity.IsConfigured);
        Assert.IsTrue(File.Exists(directory.GetPath("config.d/simplegit11.conf")));
    }

    [TestMethod]
    public async Task DeleteIdentityAsync_RemovesKeyFilesAndConfirmedConfigurationReferences()
    {
        using TemporaryDirectory directory = new();
        string privateKeyPath = directory.CreateFile("id_ed25519_example_com", "private");
        directory.CreateFile("id_ed25519_example_com.pub", $"ssh-ed25519 {Ed25519Key} test@example.com\n");
        directory.CreateFile(
            "config.d/simplegit11.conf",
            $"# SimpleGit11 identity: {privateKeyPath.Replace('\\', '/')}\nHost example.com\n    IdentityFile \"{privateKeyPath.Replace('\\', '/')}\"\n# SimpleGit11 identity end\n");
        string externalConfigPath = directory.CreateFile(
            "config.d/custom.conf",
            $"Host other\n    IdentityFile \"{privateKeyPath.Replace('\\', '/')}\"\n");
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            directory.GetPath("known_hosts"),
            (_, _, _) => Task.FromResult(new OpenSshService.ProcessResult(0, "", "")),
            sshDirectoryPath: directory.Path);

        IReadOnlyList<string> references = await service.GetIdentityConfigurationReferencesAsync(privateKeyPath);
        await service.DeleteIdentityAsync(privateKeyPath, removeExternalReferences: true);

        Assert.HasCount(2, references);
        Assert.IsFalse(File.Exists(privateKeyPath));
        Assert.IsFalse(File.Exists(privateKeyPath + ".pub"));
        Assert.IsFalse(File.ReadAllText(externalConfigPath).Contains("IdentityFile", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CreateIdentityAsync_RemoteExecution_CreatesKeyOnRemoteMachine()
    {
        RecordingRemoteHostKeyStore remoteStore = new();
        OpenSshService service = new(
            new RecordingConfirmationService(confirmed: true),
            "unused-local-known-hosts",
            (_, _, _) => throw new AssertFailedException("A local ssh-keygen process must not be used."),
            new RemoteExecutionContextService(remoteStore));

        SshIdentity identity = await service.CreateIdentityAsync(
            "git@example.com:team/repository.git",
            "");

        Assert.IsTrue(remoteStore.IdentityWasCreated);
        Assert.AreEqual("/home/test/.ssh/id_ed25519", identity.PrivateKeyPath);
    }

    [TestMethod]
    public async Task AddRemoteAsync_ChecksSshHostBeforeSavingRemote()
    {
        RecordingOpenSshService openSshService = new();
        RecordingGitCommandRunner runner = new();
        GitRemoteService service = new(
            new GitTagService(runner),
            new GitConfigService(runner),
            runner,
            openSshService: openSshService);
        RepositoryInfo repository = new(Environment.CurrentDirectory, "repository", "main");

        await service.AddRemoteAsync(repository, "origin", "git@example.com:team/repository.git");

        Assert.AreEqual("git@example.com:team/repository.git", openSshService.RemoteUrls.Single());
        CollectionAssert.AreEqual(
            new[] { "remote", "add", "origin", "git@example.com:team/repository.git" },
            runner.Invocations.Single().ToArray());
    }

    [TestMethod]
    public async Task FetchAsync_ResolvesRemoteUrlAndChecksSshHostBeforeFetch()
    {
        RecordingOpenSshService openSshService = new();
        RecordingGitCommandRunner runner = new();
        GitRemoteService service = new(
            new GitTagService(runner),
            new GitConfigService(runner),
            runner,
            openSshService: openSshService);
        RepositoryInfo repository = new(Environment.CurrentDirectory, "repository", "main");
        runner.RemoteUrl = "ssh://git@example.com/team/repository.git";

        await service.FetchAsync(repository, new GitRemote("origin", runner.RemoteUrl, runner.RemoteUrl));

        Assert.AreEqual(runner.RemoteUrl, openSshService.RemoteUrls.Single());
        Assert.AreEqual(2, runner.Invocations.Count);
        CollectionAssert.AreEqual(
            new[] { "remote", "get-url", "origin" },
            runner.Invocations[0].ToArray());
        CollectionAssert.Contains(runner.Invocations[1].ToList(), "fetch");
    }

    [TestMethod]
    public async Task CheckAccessAsync_UsesLsRemoteAfterTrustCheck()
    {
        RecordingOpenSshService openSshService = new();
        RecordingGitCommandRunner runner = new();
        GitRemoteService service = new(
            new GitTagService(runner),
            new GitConfigService(runner),
            runner,
            openSshService: openSshService);
        RepositoryInfo repository = new(Environment.CurrentDirectory, "repository", "main");
        const string url = "git@example.com:team/repository.git";

        await service.CheckAccessAsync(repository, url);

        Assert.AreEqual(url, openSshService.RemoteUrls.Single());
        CollectionAssert.AreEqual(new[] { "ls-remote", url }, runner.Invocations.Single().ToArray());
    }

    [TestMethod]
    public async Task FetchAsync_RemoteExecution_ChecksHostOnExecutionMachine()
    {
        RecordingOpenSshService openSshService = new();
        RecordingGitCommandRunner runner = new()
        {
            RemoteUrl = "git@example.com:team/repository.git"
        };
        GitRemoteService service = new(
            new GitTagService(runner),
            new GitConfigService(runner),
            runner,
            new TestExecutionContextService(new InMemoryRepositoryFileSystem()),
            openSshService);
        RepositoryInfo repository = new(Environment.CurrentDirectory, "repository", "main");

        await service.FetchAsync(repository, new GitRemote("origin", runner.RemoteUrl, runner.RemoteUrl));

        CollectionAssert.AreEqual(new[] { runner.RemoteUrl }, openSshService.RemoteUrls);
    }

    private static OpenSshService CreateService(
        RecordingConfirmationService confirmationService,
        string knownHostsPath)
    {
        return new OpenSshService(
            confirmationService,
            knownHostsPath,
            (_, arguments, _) =>
            {
                CollectionAssert.Contains(arguments.ToList(), "example.com");
                string output = $"example.com ssh-ed25519 {Ed25519Key}{Environment.NewLine}";
                return Task.FromResult(new OpenSshService.ProcessResult(0, output, ""));
            });
    }

    private sealed class RecordingConfirmationService(bool confirmed) : IHostKeyConfirmationService
    {
        public HostKeyConfirmation? Confirmation { get; private set; }

        public Task<bool> ConfirmAsync(
            HostKeyConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            Confirmation = confirmation;
            return Task.FromResult(confirmed);
        }
    }

    private sealed class RecordingOpenSshService : IOpenSshService
    {
        public List<string> RemoteUrls { get; } = [];

        public Task EnsureTrustedAsync(string remoteUrl, CancellationToken cancellationToken = default)
        {
            RemoteUrls.Add(remoteUrl);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SshIdentity>>([]);

        public Task<SshIdentity> CreateIdentityAsync(
            string remoteUrl,
            string passphrase,
            CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException("Identity creation was not expected.");
        }

        public Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
            string privateKeyPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task DeleteIdentityAsync(
            string privateKeyPath,
            bool removeExternalReferences,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingGitCommandRunner : IGitCommandRunner
    {
        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public string RemoteUrl { get; set; } = "";

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            GitCommandOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(arguments.ToArray());
            string output = arguments.Count >= 2
                && arguments[0] == "remote"
                && arguments[1] == "get-url"
                    ? RemoteUrl
                    : "";
            return Task.FromResult(new GitCommandResult(0, output, ""));
        }
    }

    private sealed class RecordingRemoteHostKeyStore :
        IRemoteSshHostKeyStore,
        IRemoteSshIdentityStore
    {
        public string ScannedHost { get; private set; } = "";
        public int ScannedPort { get; private set; }
        public IReadOnlyList<string> AppendedKeyLines { get; private set; } = [];
        public string IdentityHost { get; private set; } = "";
        public int IdentityPort { get; private set; }
        public string? IdentityUser { get; private set; }
        public bool IdentityWasCreated { get; private set; }

        public Task<string> ReadKnownHostsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("");
        }

        public Task<IReadOnlyList<string>> ScanHostKeysAsync(
            string host,
            int port,
            CancellationToken cancellationToken = default)
        {
            ScannedHost = host;
            ScannedPort = port;
            return Task.FromResult<IReadOnlyList<string>>(
                [$"{host} ssh-ed25519 {Ed25519Key}"]);
        }

        public Task AppendKnownHostsAsync(
            IReadOnlyList<string> keyLines,
            CancellationToken cancellationToken = default)
        {
            AppendedKeyLines = keyLines;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SshIdentity>>([]);

        public Task<SshIdentity> CreateIdentityAsync(
            string host,
            int port,
            string? user,
            string machineName,
            string passphrase,
            CancellationToken cancellationToken = default)
        {
            IdentityHost = host;
            IdentityPort = port;
            IdentityUser = user;
            IdentityWasCreated = true;
            return Task.FromResult(new SshIdentity(
                "/home/test/.ssh/id_ed25519",
                $"ssh-ed25519 {Ed25519Key} test@example.com",
                "SHA256:test"));
        }

        public Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
            string privateKeyPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task DeleteIdentityAsync(
            string privateKeyPath,
            bool removeExternalReferences,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RemoteExecutionContextService : IExecutionContextService
    {
        public RemoteExecutionContextService(IRemoteSshHostKeyStore hostKeyStore)
        {
            Current = new AppExecutionContext(
                Guid.NewGuid(),
                1,
                "ssh",
                null,
                new RemoteRuntime(hostKeyStore));
        }

        public AppExecutionContext Current { get; }
        public event EventHandler<ExecutionContextChangedEventArgs>? CurrentChanged { add { } remove { } }

        public Task ActivateAsync(
            string providerId,
            ExecutionConnectionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UseLocalAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RemoteRuntime(IRemoteSshHostKeyStore hostKeyStore) :
        IExecutionRuntime,
        IRemoteSshHostKeyStore,
        IRemoteSshIdentityStore
    {
        public string DisplayMachineName => "remote-server";
        public ExecutionCapabilities Capabilities => ExecutionCapabilities.Git;
        public IGitCommandRunner Git => throw new NotSupportedException();
        public IRepositoryFileSystem Files => throw new NotSupportedException();
        public IRepositoryPathService Paths => throw new NotSupportedException();
        public IRepositoryFileTransfer FileTransfer => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<string> ReadKnownHostsAsync(CancellationToken cancellationToken = default) =>
            hostKeyStore.ReadKnownHostsAsync(cancellationToken);
        public Task<IReadOnlyList<string>> ScanHostKeysAsync(
            string host,
            int port,
            CancellationToken cancellationToken = default) =>
            hostKeyStore.ScanHostKeysAsync(host, port, cancellationToken);
        public Task AppendKnownHostsAsync(
            IReadOnlyList<string> keyLines,
            CancellationToken cancellationToken = default) =>
            hostKeyStore.AppendKnownHostsAsync(keyLines, cancellationToken);
        public Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
            CancellationToken cancellationToken = default) =>
            ((IRemoteSshIdentityStore)hostKeyStore).GetIdentitiesAsync(cancellationToken);
        public Task<SshIdentity> CreateIdentityAsync(
            string host,
            int port,
            string? user,
            string machineName,
            string passphrase,
            CancellationToken cancellationToken = default) =>
            ((IRemoteSshIdentityStore)hostKeyStore).CreateIdentityAsync(
                host,
                port,
                user,
                machineName,
                passphrase,
                cancellationToken);
        public Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
            string privateKeyPath,
            CancellationToken cancellationToken = default) =>
            ((IRemoteSshIdentityStore)hostKeyStore).GetIdentityConfigurationReferencesAsync(
                privateKeyPath,
                cancellationToken);
        public Task DeleteIdentityAsync(
            string privateKeyPath,
            bool removeExternalReferences,
            CancellationToken cancellationToken = default) =>
            ((IRemoteSshIdentityStore)hostKeyStore).DeleteIdentityAsync(
                privateKeyPath,
                removeExternalReferences,
                cancellationToken);
    }
}
