using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleGit11.Plugin.Ssh.Services;

internal sealed class SshPublicKeyInstaller
{
    private const string InstallCommand =
        "umask 077 && " +
        "mkdir -p \"$HOME/.ssh\" && " +
        "touch \"$HOME/.ssh/authorized_keys\" && " +
        "chmod 700 \"$HOME/.ssh\" && " +
        "chmod 600 \"$HOME/.ssh/authorized_keys\" && " +
        "IFS= read -r key && " +
        "(grep -Fqx \"$key\" \"$HOME/.ssh/authorized_keys\" || " +
        "printf '%s\\n' \"$key\" >> \"$HOME/.ssh/authorized_keys\")";

    private readonly Func<SshConnectionSettings, SshConnectionMonitor, CancellationToken,
        Task<ISshCommandExecutor>> _connectAsync;
    private readonly ISshPrivateKeyService _privateKeyService;

    public SshPublicKeyInstaller(ISshPrivateKeyService privateKeyService)
        : this(
            privateKeyService,
            async (settings, monitor, cancellationToken) => new CommandExecutorAdapter(
                await SshCommandSession.ConnectAsync(
                    settings,
                    monitor,
                    cancellationToken)))
    {
    }

    internal SshPublicKeyInstaller(
        ISshPrivateKeyService privateKeyService,
        Func<SshConnectionSettings, SshConnectionMonitor, CancellationToken,
            Task<ISshCommandExecutor>> connectAsync)
    {
        _privateKeyService = privateKeyService
            ?? throw new ArgumentNullException(nameof(privateKeyService));
        _connectAsync = connectAsync ?? throw new ArgumentNullException(nameof(connectAsync));
    }

    public async Task InstallAsync(
        SshConnectionSettings settings,
        SshConnectionMonitor connectionMonitor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(connectionMonitor);
        if (string.IsNullOrWhiteSpace(settings.PrivateKeyPath)
            || string.IsNullOrEmpty(settings.Password))
        {
            return;
        }

        SshConnectionSettings passwordSettings = settings with
        {
            PrivateKeyPath = null,
            PrivateKeyPassphrase = null
        };
        await using ISshCommandExecutor session = await _connectAsync(
            passwordSettings,
            connectionMonitor,
            cancellationToken);

        SshCommandResult operatingSystem = await session.ExecuteAsync(
            "uname -s",
            cancellationToken: cancellationToken);
        if (operatingSystem.ExitCode != 0
            || !string.Equals(
                operatingSystem.StandardOutput.Trim(),
                "Linux",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Automatic SSH public-key installation is currently supported only on Linux hosts.");
        }

        string authorizedKey = await _privateKeyService.GetAuthorizedKeyAsync(
            settings.PrivateKeyPath,
            settings.PrivateKeyPassphrase,
            cancellationToken);
        SshCommandResult installation = await session.ExecuteAsync(
            InstallCommand,
            authorizedKey + "\n",
            cancellationToken);
        if (installation.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(installation.StandardError)
                ? "The remote command returned a non-zero exit code."
                : installation.StandardError.Trim();
            throw new InvalidOperationException(
                $"The SSH public key could not be installed on the Linux host. {details}");
        }
    }

    private sealed class CommandExecutorAdapter(SshCommandSession session) : ISshCommandExecutor
    {
        private readonly SshCommandSession _session = session;

        public Task<SshCommandResult> ExecuteAsync(
            string commandText,
            string? standardInput = null,
            CancellationToken cancellationToken = default) =>
            _session.ExecuteAsync(commandText, standardInput, cancellationToken);

        public ValueTask DisposeAsync() => _session.DisposeAsync();
    }
}
