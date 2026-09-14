using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh.Services;

internal sealed class RemoteSshIdentityStore : IRemoteSshIdentityStore
{
    private const int DefaultSshPort = 22;
    private readonly SshCommandSession _commandSession;
    private readonly SshRepositoryFileSystem _fileSystem;
    private readonly IRepositoryPathService _paths;

    public RemoteSshIdentityStore(
        SshCommandSession commandSession,
        SshRepositoryFileSystem fileSystem,
        IRepositoryPathService paths)
    {
        _commandSession = commandSession;
        _fileSystem = fileSystem;
        _paths = paths;
    }

    public async Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
        CancellationToken cancellationToken = default)
    {
        string home = await GetHomePathAsync(cancellationToken);
        IReadOnlyList<string> configPaths = await EnumerateConfigurationFilesAsync(home, cancellationToken);
        List<string> configuredPaths = [];
        foreach (string configPath in configPaths)
        {
            string content = Encoding.UTF8.GetString(await _fileSystem.ReadAllBytesAsync(configPath, cancellationToken));
            foreach (string path in ParseIdentityPaths(content))
            {
                if (path.Contains('%', StringComparison.Ordinal))
                {
                    continue;
                }

                string expanded = ExpandIdentityPath(path, "", DefaultSshPort, null, home);
                if (!configuredPaths.Contains(expanded, GetPathComparer()))
                {
                    configuredPaths.Add(expanded);
                }
            }
        }

        return await ReadIdentitiesAsync(home, configuredPaths, cancellationToken);
    }

    private async Task<IReadOnlyList<SshIdentity>> ReadIdentitiesAsync(
        string home,
        IReadOnlyList<string> configuredPaths,
        CancellationToken cancellationToken)
    {
        StringComparer pathComparer = GetPathComparer();
        HashSet<string> configuredPathSet = configuredPaths.ToHashSet(pathComparer);
        List<string> identityPaths = [.. configuredPaths];
        SshCommandResult enumerationResult = await _commandSession.ExecuteAsync(
            ComposeEnumeratePublicKeysCommand(_paths.Combine(home, ".ssh")),
            cancellationToken: cancellationToken);
        if (enumerationResult.ExitCode == 0)
        {
            foreach (string publicKeyPath in enumerationResult.StandardOutput.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!publicKeyPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string privateKeyPath = publicKeyPath[..^4];
                if (!identityPaths.Contains(privateKeyPath, pathComparer))
                {
                    identityPaths.Add(privateKeyPath);
                }
            }
        }

        List<SshIdentity> identities = [];
        foreach (string privateKeyPath in identityPaths)
        {
            string publicKeyPath = privateKeyPath + ".pub";
            if (!await _fileSystem.FileExistsAsync(privateKeyPath, cancellationToken)
                || !await _fileSystem.FileExistsAsync(publicKeyPath, cancellationToken))
            {
                continue;
            }

            string publicKey = Encoding.UTF8.GetString(
                await _fileSystem.ReadAllBytesAsync(publicKeyPath, cancellationToken));
            if (TryCreateIdentity(
                privateKeyPath,
                publicKey,
                configuredPathSet.Contains(privateKeyPath),
                out SshIdentity? identity))
            {
                identities.Add(identity!);
            }
        }

        return identities;
    }

    public async Task<SshIdentity> CreateIdentityAsync(
        string host,
        int port,
        string? user,
        string machineName,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        string home = await GetHomePathAsync(cancellationToken);
        string sshDirectory = _paths.Combine(home, ".ssh");
        string basePath = _paths.Combine(
            sshDirectory,
            $"id_ed25519_{CreateIdentityFileSuffix(host)}");
        string privateKeyPath = basePath;
        int suffix = 2;
        while (await _fileSystem.FileExistsAsync(privateKeyPath, cancellationToken)
            || await _fileSystem.FileExistsAsync(privateKeyPath + ".pub", cancellationToken))
        {
            privateKeyPath = $"{basePath}_{suffix}";
            suffix++;
        }

        string publicKeyPath = privateKeyPath + ".pub";

        if (!await _fileSystem.DirectoryExistsAsync(sshDirectory, cancellationToken))
        {
            await ExecuteRequiredAsync(ComposeCreateDirectoryCommand(sshDirectory), cancellationToken);
        }

        string comment = $"{host} on {machineName}";
        await ExecuteRequiredAsync(
            ComposeGenerateKeyCommand(privateKeyPath, comment, ""),
            cancellationToken);

        if (!await _fileSystem.FileExistsAsync(publicKeyPath, cancellationToken))
        {
            SshCommandResult publicKeyResult = await _commandSession.ExecuteAsync(
                ComposeExtractPublicKeyCommand(privateKeyPath, ""),
                cancellationToken: cancellationToken);
            if (publicKeyResult.ExitCode != 0 || string.IsNullOrWhiteSpace(publicKeyResult.StandardOutput))
            {
                throw new InvalidOperationException(publicKeyResult.StandardError.Trim());
            }

            await _fileSystem.WriteAllBytesAtomicAsync(
                publicKeyPath,
                Encoding.UTF8.GetBytes(publicKeyResult.StandardOutput.Trim() + "\n"),
                cancellationToken);
        }

        string publicKey = Encoding.UTF8.GetString(
            await _fileSystem.ReadAllBytesAsync(publicKeyPath, cancellationToken));
        if (!TryCreateIdentity(privateKeyPath, publicKey, isConfigured: false, out SshIdentity? identity))
        {
            throw new InvalidOperationException("The generated SSH public key is invalid.");
        }

        if (!string.IsNullOrEmpty(passphrase))
        {
            Exception? agentException = null;
            try
            {
                await AddIdentityToAgentAsync(home, privateKeyPath, cancellationToken);
            }
            catch (Exception exception)
            {
                agentException = exception;
            }

            await ExecuteRequiredAsync(
                ComposeEncryptIdentityCommand(privateKeyPath, passphrase),
                cancellationToken);
            if (agentException is not null)
            {
                throw new InvalidOperationException(agentException.Message, agentException);
            }
        }

        await AddIdentityConfigurationAsync(
            home,
            host,
            privateKeyPath,
            useManagedAgent: !string.IsNullOrEmpty(passphrase),
            cancellationToken);
        return identity! with { IsConfigured = true };
    }

    public async Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
        string privateKeyPath,
        CancellationToken cancellationToken = default)
    {
        string home = await GetHomePathAsync(cancellationToken);
        List<string> references = [];
        foreach (string configPath in await EnumerateConfigurationFilesAsync(home, cancellationToken))
        {
            string content = Encoding.UTF8.GetString(await _fileSystem.ReadAllBytesAsync(configPath, cancellationToken));
            if (ContainsIdentityReference(content, privateKeyPath, home))
            {
                references.Add(configPath);
            }
        }
        return references;
    }

    public async Task DeleteIdentityAsync(
        string privateKeyPath,
        bool removeExternalReferences,
        CancellationToken cancellationToken = default)
    {
        string home = await GetHomePathAsync(cancellationToken);
        string managedConfigPath = GetManagedConfigPath(home);
        foreach (string configPath in await EnumerateConfigurationFilesAsync(home, cancellationToken))
        {
            bool isManaged = GetPathComparer().Equals(configPath, managedConfigPath);
            if (!isManaged && !removeExternalReferences)
            {
                continue;
            }

            string content = Encoding.UTF8.GetString(await _fileSystem.ReadAllBytesAsync(configPath, cancellationToken));
            string updated = RemoveIdentityReferences(content, privateKeyPath, home, isManaged);
            if (!string.Equals(content, updated, StringComparison.Ordinal))
            {
                await _fileSystem.WriteAllBytesAtomicAsync(configPath, Encoding.UTF8.GetBytes(updated), cancellationToken);
            }
        }

        if (await _fileSystem.FileExistsAsync(privateKeyPath, cancellationToken))
        {
            await _fileSystem.DeleteFileAsync(privateKeyPath, cancellationToken);
        }
        if (await _fileSystem.FileExistsAsync(privateKeyPath + ".pub", cancellationToken))
        {
            await _fileSystem.DeleteFileAsync(privateKeyPath + ".pub", cancellationToken);
        }
    }

    private static string CreateIdentityFileSuffix(string host)
    {
        StringBuilder builder = new(host.Length);
        foreach (char character in host.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString().Trim('_');
    }

    private async Task<string> GetHomePathAsync(CancellationToken cancellationToken)
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

        return home;
    }

    private string ComposeCreateDirectoryCommand(string directory)
    {
        return _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"New-Item -ItemType Directory -Force -LiteralPath {QuotePowerShell(directory)} | Out-Null")
            : $"umask 077; mkdir -p -- {QuotePosix(directory)}; chmod 700 -- {QuotePosix(directory)}";
    }

    private string ComposeEnumeratePublicKeysCommand(string directory)
    {
        return _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"if (Test-Path -LiteralPath {QuotePowerShell(directory)}) {{ Get-ChildItem -LiteralPath {QuotePowerShell(directory)} -Filter '*.pub' -File | ForEach-Object {{ [Console]::Out.WriteLine($_.FullName) }} }}")
            : $"if [ -d {QuotePosix(directory)} ]; then find {QuotePosix(directory)} -maxdepth 1 -type f -name '*.pub' -print; fi";
    }

    private string ComposeGenerateKeyCommand(string privateKeyPath, string comment, string passphrase)
    {
        return _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"& ssh-keygen '-q' '-t' 'ed25519' '-N' {QuotePowerShell(passphrase)} '-C' {QuotePowerShell(comment)} '-f' {QuotePowerShell(privateKeyPath)}; exit $LASTEXITCODE")
            : $"umask 077; ssh-keygen -q -t ed25519 -N {QuotePosix(passphrase)} -C {QuotePosix(comment)} -f {QuotePosix(privateKeyPath)}";
    }

    private string ComposeExtractPublicKeyCommand(string privateKeyPath, string passphrase)
    {
        return _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"& ssh-keygen '-y' '-P' {QuotePowerShell(passphrase)} '-f' {QuotePowerShell(privateKeyPath)}; exit $LASTEXITCODE")
            : $"ssh-keygen -y -P {QuotePosix(passphrase)} -f {QuotePosix(privateKeyPath)}";
    }

    private string ComposeEncryptIdentityCommand(string privateKeyPath, string passphrase)
    {
        return _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"& ssh-keygen '-q' '-p' '-P' '' '-N' {QuotePowerShell(passphrase)} '-f' {QuotePowerShell(privateKeyPath)}; exit $LASTEXITCODE")
            : $"ssh-keygen -q -p -P '' -N {QuotePosix(passphrase)} -f {QuotePosix(privateKeyPath)}";
    }

    private async Task<IReadOnlyList<string>> EnumerateConfigurationFilesAsync(
        string home,
        CancellationToken cancellationToken)
    {
        string sshDirectory = _paths.Combine(home, ".ssh");
        string command = _paths.Style == RepositoryPathStyle.Windows
            ? ComposePowerShell(
                $"$files=@(); $main={QuotePowerShell(_paths.Combine(sshDirectory, "config"))}; if (Test-Path -LiteralPath $main) {{$files+=$main}}; $dir={QuotePowerShell(_paths.Combine(sshDirectory, "config.d"))}; if (Test-Path -LiteralPath $dir) {{$files+=Get-ChildItem -LiteralPath $dir -File | ForEach-Object {{$_.FullName}}}}; $files | ForEach-Object {{[Console]::Out.WriteLine($_)}}")
            : $"if [ -f {QuotePosix(_paths.Combine(sshDirectory, "config"))} ]; then printf '%s\\n' {QuotePosix(_paths.Combine(sshDirectory, "config"))}; fi; if [ -d {QuotePosix(_paths.Combine(sshDirectory, "config.d"))} ]; then find {QuotePosix(_paths.Combine(sshDirectory, "config.d"))} -maxdepth 1 -type f -print; fi";
        SshCommandResult result = await _commandSession.ExecuteAsync(command, cancellationToken: cancellationToken);
        return result.ExitCode == 0
            ? result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    private async Task AddIdentityConfigurationAsync(
        string home,
        string host,
        string privateKeyPath,
        bool useManagedAgent,
        CancellationToken cancellationToken)
    {
        string sshDirectory = _paths.Combine(home, ".ssh");
        string configDirectory = _paths.Combine(sshDirectory, "config.d");
        if (!await _fileSystem.DirectoryExistsAsync(configDirectory, cancellationToken))
        {
            await ExecuteRequiredAsync(ComposeCreateDirectoryCommand(configDirectory), cancellationToken);
        }

        string mainConfigPath = _paths.Combine(sshDirectory, "config");
        string mainContent = await _fileSystem.FileExistsAsync(mainConfigPath, cancellationToken)
            ? Encoding.UTF8.GetString(await _fileSystem.ReadAllBytesAsync(mainConfigPath, cancellationToken))
            : "";
        const string includeDirective = "Include config.d/simplegit11.conf";
        if (!mainContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), includeDirective, StringComparison.OrdinalIgnoreCase)))
        {
            string prefix = mainContent.Length == 0 || mainContent.EndsWith('\n') ? "" : "\n";
            await _fileSystem.WriteAllBytesAtomicAsync(
                mainConfigPath,
                Encoding.UTF8.GetBytes(includeDirective + "\n" + prefix + mainContent),
                cancellationToken);
        }

        string managedPath = GetManagedConfigPath(home);
        string managedContent = await _fileSystem.FileExistsAsync(managedPath, cancellationToken)
            ? Encoding.UTF8.GetString(await _fileSystem.ReadAllBytesAsync(managedPath, cancellationToken))
            : "";
        if (ContainsIdentityReference(managedContent, privateKeyPath, home))
        {
            return;
        }

        string normalizedPath = privateKeyPath.Replace('\\', '/');
        string agentLine = useManagedAgent && _paths.Style == RepositoryPathStyle.Posix
            ? "    IdentityAgent ~/.ssh/simplegit11-agent.sock\n"
            : "";
        string block = $"# SimpleGit11 identity: {normalizedPath}\n" +
            $"Host {host}\n" +
            $"    IdentityFile \"{normalizedPath}\"\n" +
            "    IdentitiesOnly yes\n" + agentLine +
            "# SimpleGit11 identity end\n";
        string separator = managedContent.Length == 0 || managedContent.EndsWith('\n') ? "" : "\n";
        await _fileSystem.WriteAllBytesAtomicAsync(
            managedPath,
            Encoding.UTF8.GetBytes(managedContent + separator + block),
            cancellationToken);
    }

    private async Task AddIdentityToAgentAsync(
        string home,
        string privateKeyPath,
        CancellationToken cancellationToken)
    {
        string command;
        if (_paths.Style == RepositoryPathStyle.Windows)
        {
            command = ComposePowerShell(
                $"$service=Get-Service ssh-agent; if ($service.Status -ne 'Running') {{Start-Service ssh-agent}}; & ssh-add {QuotePowerShell(privateKeyPath)}; exit $LASTEXITCODE");
        }
        else
        {
            string socket = _paths.Combine(_paths.Combine(home, ".ssh"), "simplegit11-agent.sock");
            command = $"if [ ! -S {QuotePosix(socket)} ]; then rm -f -- {QuotePosix(socket)}; ssh-agent -a {QuotePosix(socket)} >/dev/null; fi; SSH_AUTH_SOCK={QuotePosix(socket)} ssh-add {QuotePosix(privateKeyPath)}";
        }

        SshCommandResult result = await _commandSession.ExecuteAsync(
            command,
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "The SSH key could not be added to ssh-agent."
                : result.StandardError.Trim());
        }
    }

    private StringComparer GetPathComparer() => _paths.Style == RepositoryPathStyle.Windows
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private string GetManagedConfigPath(string home) =>
        _paths.Combine(_paths.Combine(_paths.Combine(home, ".ssh"), "config.d"), "simplegit11.conf");

    private bool ContainsIdentityReference(string content, string privateKeyPath, string home) =>
        ParseIdentityPaths(content).Any(path => !path.Contains('%', StringComparison.Ordinal)
            && GetPathComparer().Equals(
                ExpandIdentityPath(path, "", DefaultSshPort, null, home),
                privateKeyPath));

    private string RemoveIdentityReferences(
        string content,
        string privateKeyPath,
        string home,
        bool removeManagedBlock)
    {
        List<string> output = [];
        bool skip = false;
        foreach (string line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (removeManagedBlock && line.StartsWith("# SimpleGit11 identity: ", StringComparison.Ordinal)
                && GetPathComparer().Equals(line["# SimpleGit11 identity: ".Length..].Trim(), privateKeyPath.Replace('\\', '/')))
            {
                skip = true;
                continue;
            }
            if (skip)
            {
                if (line.StartsWith("# SimpleGit11 identity end", StringComparison.Ordinal))
                {
                    skip = false;
                }
                continue;
            }
            string trimmed = line.Trim();
            if (!removeManagedBlock && trimmed.StartsWith("IdentityFile", StringComparison.OrdinalIgnoreCase)
                && !trimmed["IdentityFile".Length..].Contains('%', StringComparison.Ordinal)
                && GetPathComparer().Equals(
                    ExpandIdentityPath(trimmed["IdentityFile".Length..].Trim(), "", DefaultSshPort, null, home),
                    privateKeyPath))
            {
                continue;
            }
            output.Add(line);
        }
        return string.Join("\n", output);
    }

    private async Task ExecuteRequiredAsync(string command, CancellationToken cancellationToken)
    {
        SshCommandResult result = await _commandSession.ExecuteAsync(command, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.StandardError.Trim());
        }
    }

    private string ExpandIdentityPath(
        string configuredPath,
        string host,
        int port,
        string? user,
        string home)
    {
        string path = configuredPath.Trim().Trim('"')
            .Replace("%d", home, StringComparison.Ordinal)
            .Replace("%h", host, StringComparison.Ordinal)
            .Replace("%p", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%r", user ?? "git", StringComparison.Ordinal);
        if (path == "~")
        {
            return home;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return _paths.Combine(home, path[2..]);
        }

        return path;
    }

    private static IReadOnlyList<string> ParseIdentityPaths(string configuration)
    {
        List<string> paths = [];
        foreach (string line in configuration.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 2
                && string.Equals(fields[0], "identityfile", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fields[1], "none", StringComparison.OrdinalIgnoreCase)
                && !paths.Contains(fields[1], StringComparer.Ordinal))
            {
                paths.Add(fields[1]);
            }
        }

        return paths;
    }

    private static bool TryCreateIdentity(
        string privateKeyPath,
        string publicKeyContent,
        bool isConfigured,
        out SshIdentity? identity)
    {
        identity = null;
        string line = publicKeyContent
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim())
            .FirstOrDefault(static value => value.Length > 0) ?? "";
        string[] fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            return false;
        }

        try
        {
            byte[] key = Convert.FromBase64String(fields[1]);
            string hash = Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
            identity = new SshIdentity(privateKeyPath, line, $"SHA256:{hash}", isConfigured);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
