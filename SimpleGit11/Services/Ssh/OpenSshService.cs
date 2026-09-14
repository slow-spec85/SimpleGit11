using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Services;

public sealed class OpenSshService : IOpenSshService
{
    private const int DefaultSshPort = 22;
    private readonly IHostKeyConfirmationService _confirmationService;
    private readonly IExecutionContextService? _executionContextService;
    private readonly string _knownHostsPath;
    private readonly string _sshDirectoryPath;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _runProcessAsync;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public OpenSshService(
        IHostKeyConfirmationService confirmationService,
        IExecutionContextService executionContextService)
        : this(
            confirmationService,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "known_hosts"),
            RunProcessAsync,
            executionContextService,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"))
    {
    }

    internal OpenSshService(
        IHostKeyConfirmationService confirmationService,
        string knownHostsPath,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> runProcessAsync,
        IExecutionContextService? executionContextService = null,
        string? sshDirectoryPath = null)
    {
        _confirmationService = confirmationService;
        _knownHostsPath = knownHostsPath;
        _sshDirectoryPath = sshDirectoryPath
            ?? Path.GetDirectoryName(knownHostsPath)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        _runProcessAsync = runProcessAsync;
        _executionContextService = executionContextService;
    }

    public async Task<IReadOnlyList<SshIdentity>> GetIdentitiesAsync(
        CancellationToken cancellationToken = default)
    {
        IRemoteSshIdentityStore? remoteStore = GetRemoteIdentityStore();
        if (_executionContextService is { Current.IsLocal: false })
        {
            return remoteStore is null
                ? []
                : await remoteStore.GetIdentitiesAsync(cancellationToken);
        }

        IReadOnlyList<string> configuredPaths = await GetConfiguredLocalIdentityPathsAsync(cancellationToken);
        return await ReadLocalIdentitiesAsync(configuredPaths, cancellationToken);
    }

    public async Task EnsureTrustedAsync(
        string remoteUrl,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseEndpoint(remoteUrl, out HostEndpoint endpoint))
        {
            return;
        }

        bool isRemoteExecution = _executionContextService is { Current.IsLocal: false };
        IRemoteSshHostKeyStore? remoteStore = GetRemoteStore();
        if (isRemoteExecution && remoteStore is null)
        {
            return;
        }

        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            if (await ContainsHostAsync(endpoint, remoteStore, cancellationToken))
            {
                return;
            }

            IReadOnlyList<string> keyLines = remoteStore is null
                ? await ScanHostKeysAsync(endpoint, cancellationToken)
                : await remoteStore.ScanHostKeysAsync(endpoint.Host, endpoint.Port, cancellationToken);
            if (keyLines.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> fingerprints = keyLines
                .Select(CreateFingerprint)
                .Where(static fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (fingerprints.Count == 0)
            {
                return;
            }

            bool confirmed = await _confirmationService.ConfirmAsync(
                new HostKeyConfirmation(endpoint.Host, endpoint.Port, fingerprints),
                cancellationToken);
            if (!confirmed || await ContainsHostAsync(endpoint, remoteStore, cancellationToken))
            {
                return;
            }

            if (remoteStore is not null)
            {
                await remoteStore.AppendKnownHostsAsync(keyLines, cancellationToken);
            }
            else
            {
                string? directory = Path.GetDirectoryName(_knownHostsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string content = string.Join(Environment.NewLine, keyLines) + Environment.NewLine;
                await File.AppendAllTextAsync(
                    _knownHostsPath,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task<SshIdentity> CreateIdentityAsync(
        string remoteUrl,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseEndpoint(remoteUrl, out HostEndpoint endpoint))
        {
            throw new ArgumentException("The remote URL is not an SSH URL.", nameof(remoteUrl));
        }

        IRemoteSshIdentityStore? remoteStore = GetRemoteIdentityStore();
        if (_executionContextService is { Current.IsLocal: false })
        {
            if (remoteStore is null)
            {
                throw new InvalidOperationException("The selected execution context cannot manage SSH identities.");
            }

            return await remoteStore.CreateIdentityAsync(
                endpoint.Host,
                endpoint.Port,
                endpoint.User,
                _executionContextService.Current.DisplayMachineName,
                passphrase,
                cancellationToken);
        }

        Directory.CreateDirectory(_sshDirectoryPath);
        string privateKeyPath = GetUniqueLocalIdentityPath(endpoint.Host);

        string machineName = _executionContextService?.Current.DisplayMachineName ?? Environment.MachineName;
        string comment = $"{endpoint.Host} on {machineName}";
        ProcessResult generationResult = await _runProcessAsync(
            "ssh-keygen",
            ["-q", "-t", "ed25519", "-N", "", "-C", comment, "-f", privateKeyPath],
            cancellationToken);
        if (generationResult.ExitCode != 0)
        {
            throw new InvalidOperationException(generationResult.StandardError.Trim());
        }

        string publicKeyPath = privateKeyPath + ".pub";
        if (!File.Exists(publicKeyPath))
        {
            ProcessResult publicKeyResult = await _runProcessAsync(
                "ssh-keygen",
                ["-y", "-P", "", "-f", privateKeyPath],
                cancellationToken);
            if (publicKeyResult.ExitCode != 0 || string.IsNullOrWhiteSpace(publicKeyResult.StandardOutput))
            {
                throw new InvalidOperationException(publicKeyResult.StandardError.Trim());
            }

            await File.WriteAllTextAsync(
                publicKeyPath,
                publicKeyResult.StandardOutput.Trim() + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }

        string publicKey = await File.ReadAllTextAsync(publicKeyPath, cancellationToken);
        if (!TryCreateIdentity(privateKeyPath, publicKey, isConfigured: false, out SshIdentity? identity))
        {
            throw new InvalidOperationException("The generated SSH public key is invalid.");
        }

        if (!string.IsNullOrEmpty(passphrase))
        {
            Exception? agentException = null;
            try
            {
                await AddLocalIdentityToAgentAsync(privateKeyPath, cancellationToken);
            }
            catch (Exception exception)
            {
                agentException = exception;
            }

            await EncryptLocalIdentityAsync(privateKeyPath, passphrase, cancellationToken);
            if (agentException is not null)
            {
                throw new InvalidOperationException(agentException.Message, agentException);
            }
        }

        await AddLocalIdentityConfigurationAsync(endpoint.Host, privateKeyPath, cancellationToken);
        return identity! with { IsConfigured = true };
    }

    public async Task<IReadOnlyList<string>> GetIdentityConfigurationReferencesAsync(
        string privateKeyPath,
        CancellationToken cancellationToken = default)
    {
        IRemoteSshIdentityStore? remoteStore = GetRemoteIdentityStore();
        if (_executionContextService is { Current.IsLocal: false })
        {
            return remoteStore is null
                ? []
                : await remoteStore.GetIdentityConfigurationReferencesAsync(privateKeyPath, cancellationToken);
        }

        List<string> references = [];
        foreach (string configPath in EnumerateLocalSshConfigFiles())
        {
            string content = await File.ReadAllTextAsync(configPath, cancellationToken);
            if (ContainsIdentityReference(content, privateKeyPath))
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
        IRemoteSshIdentityStore? remoteStore = GetRemoteIdentityStore();
        if (_executionContextService is { Current.IsLocal: false })
        {
            if (remoteStore is null)
            {
                throw new InvalidOperationException("The selected execution context cannot manage SSH identities.");
            }

            await remoteStore.DeleteIdentityAsync(privateKeyPath, removeExternalReferences, cancellationToken);
            return;
        }

        string managedConfigPath = GetManagedConfigPath();
        foreach (string configPath in EnumerateLocalSshConfigFiles())
        {
            bool isManaged = string.Equals(configPath, managedConfigPath, StringComparison.OrdinalIgnoreCase);
            if (!isManaged && !removeExternalReferences)
            {
                continue;
            }

            string content = await File.ReadAllTextAsync(configPath, cancellationToken);
            string updated = RemoveIdentityReferences(content, privateKeyPath, isManaged);
            if (!string.Equals(content, updated, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(configPath, updated, new UTF8Encoding(false), cancellationToken);
            }
        }

        if (File.Exists(privateKeyPath))
        {
            File.Delete(privateKeyPath);
        }

        if (File.Exists(privateKeyPath + ".pub"))
        {
            File.Delete(privateKeyPath + ".pub");
        }
    }

    private async Task<IReadOnlyList<SshIdentity>> ReadLocalIdentitiesAsync(
        IReadOnlyList<string> configuredPaths,
        CancellationToken cancellationToken)
    {
        HashSet<string> configuredPathSet = configuredPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> identityPaths = [.. configuredPaths];
        if (Directory.Exists(_sshDirectoryPath))
        {
            foreach (string publicKeyPath in Directory.EnumerateFiles(_sshDirectoryPath, "*.pub", SearchOption.TopDirectoryOnly))
            {
                string identityPath = publicKeyPath[..^4];
                if (!identityPaths.Contains(identityPath, StringComparer.OrdinalIgnoreCase))
                {
                    identityPaths.Add(identityPath);
                }
            }
        }

        List<SshIdentity> identities = [];
        foreach (string identityPath in identityPaths)
        {
            if (!File.Exists(identityPath) || !File.Exists(identityPath + ".pub"))
            {
                continue;
            }

            string publicKey = await File.ReadAllTextAsync(identityPath + ".pub", cancellationToken);
            if (TryCreateIdentity(
                identityPath,
                publicKey,
                configuredPathSet.Contains(identityPath),
                out SshIdentity? identity))
            {
                identities.Add(identity!);
            }
        }

        return identities;
    }

    private async Task<IReadOnlyList<string>> GetConfiguredLocalIdentityPathsAsync(
        CancellationToken cancellationToken)
    {
        List<string> paths = [];
        foreach (string configPath in EnumerateLocalSshConfigFiles())
        {
            string content = await File.ReadAllTextAsync(configPath, cancellationToken);
            foreach (string line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 2 || !string.Equals(fields[0], "IdentityFile", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? path = TryExpandConfiguredIdentityPath(fields[1]);
                if (path is null)
                {
                    continue;
                }

                if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    private IEnumerable<string> EnumerateLocalSshConfigFiles()
    {
        string configPath = Path.Combine(_sshDirectoryPath, "config");
        if (File.Exists(configPath))
        {
            yield return configPath;
        }

        string configDirectory = Path.Combine(_sshDirectoryPath, "config.d");
        if (!Directory.Exists(configDirectory))
        {
            yield break;
        }

        foreach (string path in Directory.EnumerateFiles(configDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }
    }

    private string? TryExpandConfiguredIdentityPath(string configuredPath)
    {
        string value = configuredPath.Trim().Trim('"');
        if (value.Length == 0 || value.Contains('%', StringComparison.Ordinal))
        {
            return null;
        }

        if (value == "~")
        {
            return Path.GetFullPath(_sshDirectoryPath);
        }

        if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(_sshDirectoryPath, value[2..]));
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private async Task AddLocalIdentityConfigurationAsync(
        string host,
        string privateKeyPath,
        CancellationToken cancellationToken)
    {
        string configDirectory = Path.Combine(_sshDirectoryPath, "config.d");
        Directory.CreateDirectory(configDirectory);
        string mainConfigPath = Path.Combine(_sshDirectoryPath, "config");
        string mainContent = File.Exists(mainConfigPath)
            ? await File.ReadAllTextAsync(mainConfigPath, cancellationToken)
            : "";
        const string includeDirective = "Include config.d/simplegit11.conf";
        if (!mainContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), includeDirective, StringComparison.OrdinalIgnoreCase)))
        {
            string prefix = mainContent.Length == 0 || mainContent.EndsWith('\n') ? "" : Environment.NewLine;
            mainContent = includeDirective + Environment.NewLine + prefix + mainContent;
            await File.WriteAllTextAsync(mainConfigPath, mainContent, new UTF8Encoding(false), cancellationToken);
        }

        string managedConfigPath = GetManagedConfigPath();
        string managedContent = File.Exists(managedConfigPath)
            ? await File.ReadAllTextAsync(managedConfigPath, cancellationToken)
            : "";
        if (!ContainsIdentityReference(managedContent, privateKeyPath))
        {
            string normalizedPath = privateKeyPath.Replace('\\', '/');
            string block = $"# SimpleGit11 identity: {normalizedPath}{Environment.NewLine}" +
                $"Host {host}{Environment.NewLine}" +
                $"    IdentityFile \"{normalizedPath}\"{Environment.NewLine}" +
                $"    IdentitiesOnly yes{Environment.NewLine}" +
                $"# SimpleGit11 identity end{Environment.NewLine}";
            string separator = managedContent.Length == 0 || managedContent.EndsWith('\n')
                ? ""
                : Environment.NewLine;
            await File.WriteAllTextAsync(
                managedConfigPath,
                managedContent + separator + block,
                new UTF8Encoding(false),
                cancellationToken);
        }
    }

    private async Task AddLocalIdentityToAgentAsync(
        string privateKeyPath,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await _runProcessAsync(
            "ssh-add",
            [privateKeyPath],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            await _runProcessAsync(
                "powershell.exe",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Service ssh-agent"],
                cancellationToken);
            result = await _runProcessAsync(
                "ssh-add",
                [privateKeyPath],
                cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "The SSH key could not be added to ssh-agent."
                : result.StandardError.Trim());
        }
    }

    private async Task EncryptLocalIdentityAsync(
        string privateKeyPath,
        string passphrase,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await _runProcessAsync(
            "ssh-keygen",
            ["-q", "-p", "-P", "", "-N", passphrase, "-f", privateKeyPath],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "The SSH private key could not be encrypted."
                : result.StandardError.Trim());
        }
    }

    private string GetManagedConfigPath() =>
        Path.Combine(_sshDirectoryPath, "config.d", "simplegit11.conf");

    private bool ContainsIdentityReference(string content, string privateKeyPath)
    {
        return content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("IdentityFile", StringComparison.OrdinalIgnoreCase))
            .Select(static line => line["IdentityFile".Length..].Trim())
            .Any(value => string.Equals(
                TryExpandConfiguredIdentityPath(value),
                Path.GetFullPath(privateKeyPath),
                StringComparison.OrdinalIgnoreCase));
    }

    private string RemoveIdentityReferences(string content, string privateKeyPath, bool removeManagedBlock)
    {
        List<string> lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        List<string> output = [];
        bool skippingManagedBlock = false;
        foreach (string line in lines)
        {
            if (removeManagedBlock && line.StartsWith("# SimpleGit11 identity: ", StringComparison.Ordinal)
                && string.Equals(
                    line["# SimpleGit11 identity: ".Length..].Trim(),
                    privateKeyPath.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                skippingManagedBlock = true;
                continue;
            }

            if (skippingManagedBlock)
            {
                if (line.StartsWith("# SimpleGit11 identity end", StringComparison.Ordinal))
                {
                    skippingManagedBlock = false;
                }
                continue;
            }

            string trimmed = line.Trim();
            if (!removeManagedBlock
                && trimmed.StartsWith("IdentityFile", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    TryExpandConfiguredIdentityPath(trimmed["IdentityFile".Length..].Trim()),
                    Path.GetFullPath(privateKeyPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            output.Add(line);
        }

        return string.Join(Environment.NewLine, output);
    }

    private string GetUniqueLocalIdentityPath(string host)
    {
        string basePath = Path.Combine(_sshDirectoryPath, $"id_ed25519_{CreateIdentityFileSuffix(host)}");
        string candidate = basePath;
        int suffix = 2;
        while (File.Exists(candidate) || File.Exists(candidate + ".pub"))
        {
            candidate = $"{basePath}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    internal static string CreateIdentityFileSuffix(string host)
    {
        StringBuilder builder = new(host.Length);
        foreach (char character in host.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString().Trim('_');
    }

    internal static bool TryParseEndpoint(string remoteUrl, out HostEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri))
        {
            if (!string.Equals(uri.Scheme, "ssh", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }

            string? user = string.IsNullOrWhiteSpace(uri.UserInfo)
                ? null
                : Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]);
            endpoint = new HostEndpoint(
                uri.Host.ToLowerInvariant(),
                uri.IsDefaultPort ? DefaultSshPort : uri.Port,
                user);
            return true;
        }

        int colonIndex = remoteUrl.IndexOf(':');
        if (colonIndex <= 0
            || colonIndex == 1 && char.IsLetter(remoteUrl[0])
            || remoteUrl[..colonIndex].Contains('/')
            || remoteUrl[..colonIndex].Contains('\\'))
        {
            return false;
        }

        string authority = remoteUrl[..colonIndex];
        int atIndex = authority.LastIndexOf('@');
        string host = atIndex >= 0 ? authority[(atIndex + 1)..] : authority;
        if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace))
        {
            return false;
        }

        string? scpUser = atIndex > 0 ? authority[..atIndex] : null;
        endpoint = new HostEndpoint(host.ToLowerInvariant(), DefaultSshPort, scpUser);
        return true;
    }

    internal static bool TryCreateIdentity(
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

    private async Task<bool> ContainsHostAsync(
        HostEndpoint endpoint,
        IRemoteSshHostKeyStore? remoteStore,
        CancellationToken cancellationToken)
    {
        string content;
        if (remoteStore is not null)
        {
            content = await remoteStore.ReadKnownHostsAsync(cancellationToken);
        }
        else if (File.Exists(_knownHostsPath))
        {
            content = await File.ReadAllTextAsync(_knownHostsPath, cancellationToken);
        }
        else
        {
            return false;
        }

        string lookupName = endpoint.LookupName;
        foreach (string rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            int hostsIndex = fields.Length > 0 && fields[0].StartsWith('@') ? 1 : 0;
            if (fields.Length <= hostsIndex)
            {
                continue;
            }

            foreach (string pattern in fields[hostsIndex].Split(','))
            {
                if (MatchesHost(pattern, lookupName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IRemoteSshHostKeyStore? GetRemoteStore()
    {
        return _executionContextService is { Current.IsLocal: false }
            ? _executionContextService.Current.Runtime as IRemoteSshHostKeyStore
            : null;
    }

    private IRemoteSshIdentityStore? GetRemoteIdentityStore()
    {
        return _executionContextService is { Current.IsLocal: false }
            ? _executionContextService.Current.Runtime as IRemoteSshIdentityStore
            : null;
    }

    private async Task<IReadOnlyList<string>> ScanHostKeysAsync(
        HostEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["-T", "10"];
        if (endpoint.Port != DefaultSshPort)
        {
            arguments.Add("-p");
            arguments.Add(endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        arguments.Add(endpoint.Host);
        ProcessResult result = await _runProcessAsync("ssh-keyscan", arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && line[0] != '#' && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string CreateFingerprint(string keyLine)
    {
        string[] fields = keyLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3)
        {
            return "";
        }

        try
        {
            byte[] key = Convert.FromBase64String(fields[2]);
            string hash = Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
            return $"{fields[1]} SHA256:{hash}";
        }
        catch (FormatException)
        {
            return "";
        }
    }

    private static bool MatchesHost(string pattern, string lookupName)
    {
        if (pattern.StartsWith("|1|", StringComparison.Ordinal))
        {
            string[] parts = pattern.Split('|');
            if (parts.Length != 4)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                using HMACSHA1 hmac = new(salt);
                byte[] actual = hmac.ComputeHash(Encoding.UTF8.GetBytes(lookupName));
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return string.Equals(pattern, lookupName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{fileName} could not be started.");
        try
        {
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw;
        }
    }

    internal readonly record struct HostEndpoint(string Host, int Port, string? User)
    {
        public string LookupName => Port == DefaultSshPort ? Host : $"[{Host}]:{Port}";
    }

    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
