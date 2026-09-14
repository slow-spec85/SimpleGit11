using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

internal sealed class RemoteSshHostKeyStore : IRemoteSshHostKeyStore
{
    private const int DefaultSshPort = 22;
    private readonly SshCommandSession _commandSession;
    private readonly SshRepositoryFileSystem _fileSystem;
    private readonly IRepositoryPathService _paths;

    public RemoteSshHostKeyStore(
        SshCommandSession commandSession,
        SshRepositoryFileSystem fileSystem,
        IRepositoryPathService paths)
    {
        _commandSession = commandSession;
        _fileSystem = fileSystem;
        _paths = paths;
    }

    public async Task<string> ReadKnownHostsAsync(CancellationToken cancellationToken = default)
    {
        string path = await GetKnownHostsPathAsync(cancellationToken);
        return await _fileSystem.FileExistsAsync(path, cancellationToken)
            ? Encoding.UTF8.GetString(await _fileSystem.ReadAllBytesAsync(path, cancellationToken))
            : "";
    }

    public async Task<IReadOnlyList<string>> ScanHostKeysAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string command = ComposeSshKeyScan(host, port);
        SshCommandResult result = await _commandSession.ExecuteAsync(command, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            return [];
        }

        List<string> lines = [];
        foreach (string rawLine in result.StandardOutput.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length > 0
                && line[0] != '#'
                && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public async Task AppendKnownHostsAsync(
        IReadOnlyList<string> keyLines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyLines);
        foreach (string line in keyLines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Contains('\r') || line.Contains('\n'))
            {
                throw new ArgumentException("An SSH host key line is invalid.", nameof(keyLines));
            }
        }

        string path = await GetKnownHostsPathAsync(cancellationToken);
        string directory = _paths.GetParent(path)
            ?? throw new InvalidOperationException("The remote .ssh directory could not be resolved.");
        if (!await _fileSystem.DirectoryExistsAsync(directory, cancellationToken))
        {
            SshCommandResult createResult = await _commandSession.ExecuteAsync(
                ComposeCreateDirectory(directory),
                cancellationToken: cancellationToken);
            if (createResult.ExitCode != 0)
            {
                throw new InvalidOperationException(createResult.StandardError.Trim());
            }
        }

        string existing = await ReadKnownHostsAsync(cancellationToken);
        StringBuilder content = new(existing);
        if (content.Length > 0 && content[^1] is not ('\r' or '\n'))
        {
            content.AppendLine();
        }

        foreach (string line in keyLines)
        {
            content.AppendLine(line);
        }

        await _fileSystem.WriteAllBytesAtomicAsync(
            path,
            Encoding.UTF8.GetBytes(content.ToString()),
            cancellationToken);
        if (_paths.Style == RepositoryPathStyle.Posix)
        {
            await _commandSession.ExecuteAsync(
                $"chmod 600 -- {QuotePosix(path)}",
                cancellationToken: cancellationToken);
        }
    }

    private async Task<string> GetKnownHostsPathAsync(CancellationToken cancellationToken)
    {
        string command = _paths.Style == RepositoryPathStyle.Posix
            ? "printf '%s' \"$HOME\""
            : ComposePowerShell("[Console]::Out.Write([Environment]::GetFolderPath('UserProfile'))");
        SshCommandResult result = await _commandSession.ExecuteAsync(command, cancellationToken: cancellationToken);
        string home = result.StandardOutput.Trim();
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("The remote user profile directory could not be resolved.");
        }

        return _paths.Combine(_paths.Combine(home, ".ssh"), "known_hosts");
    }

    private string ComposeSshKeyScan(string host, int port)
    {
        if (_paths.Style == RepositoryPathStyle.Windows)
        {
            string portArgument = port == DefaultSshPort ? "" : $" -p {port}";
            return ComposePowerShell(
                $"& ssh-keyscan -T 10{portArgument} {QuotePowerShell(host)}; exit $LASTEXITCODE");
        }

        string posixPortArgument = port == DefaultSshPort ? "" : $" -p {port}";
        return $"ssh-keyscan -T 10{posixPortArgument} {QuotePosix(host)}";
    }

    private string ComposeCreateDirectory(string directory)
    {
        return _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"New-Item -ItemType Directory -Force -LiteralPath {QuotePowerShell(directory)} | Out-Null")
            : $"umask 077; mkdir -p -- {QuotePosix(directory)}";
    }

    private static string ComposePowerShell(string script)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "$ErrorActionPreference='Stop'; " + script));
        return $"powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
    }

    private static string QuotePosix(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static string QuotePowerShell(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
