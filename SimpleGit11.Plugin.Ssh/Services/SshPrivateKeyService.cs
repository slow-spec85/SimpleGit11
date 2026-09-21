using System.Diagnostics;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Security;

namespace SimpleGit11.Plugin.Ssh.Services;

internal sealed class SshPrivateKeyService : ISshPrivateKeyService
{
    public async Task GenerateAsync(
        string path,
        string? passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || File.Exists(fullPath + ".pub"))
        {
            throw new IOException("The SSH key path or its public-key file already exists.");
        }

        string keygenPath = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh-keygen.exe");
        if (!File.Exists(keygenPath))
        {
            throw new FileNotFoundException("OpenSSH ssh-keygen is required to create an ED25519 key.", keygenPath);
        }

        ProcessStartInfo startInfo = new(keygenPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("ed25519");
        startInfo.ArgumentList.Add("-N");
        startInfo.ArgumentList.Add(passphrase ?? "");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(fullPath);

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        Task<string> errorOutput = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
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

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The ED25519 SSH key could not be generated. {await errorOutput}");
        }
    }

    public Task<bool> RequiresPassphraseAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(
            () =>
            {
                if (HasEncryptedPemHeader(path))
                {
                    return true;
                }

                try
                {
                    using PrivateKeyFile privateKey = new(path);
                    return false;
                }
                catch (SshPassPhraseNullOrEmptyException)
                {
                    return true;
                }
            },
            cancellationToken);
    }

    public Task<bool> CanOpenAsync(
        string path,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        return Task.Run(
            () =>
            {
                try
                {
                    using PrivateKeyFile privateKey = new(path, passphrase);
                    return true;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return false;
                }
            },
            cancellationToken);
    }

    public Task<string> GetAuthorizedKeyAsync(
        string path,
        string? passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(
            () =>
            {
                using PrivateKeyFile privateKey = passphrase is null
                    ? new PrivateKeyFile(path)
                    : new PrivateKeyFile(path, passphrase);
                HostAlgorithm algorithm = privateKey.HostKeyAlgorithms.First();
                return $"{algorithm.Name} {Convert.ToBase64String(algorithm.Data)}";
            },
            cancellationToken);
    }

    private static bool HasEncryptedPemHeader(string path)
    {
        using StreamReader reader = File.OpenText(path);
        for (int lineIndex = 0; lineIndex < 4; lineIndex++)
        {
            string? line = reader.ReadLine();
            if (line is null)
            {
                return false;
            }

            string trimmedLine = line.Trim();
            if (string.Equals(
                    trimmedLine,
                    "-----BEGIN ENCRYPTED PRIVATE KEY-----",
                    StringComparison.Ordinal)
                || (trimmedLine.StartsWith("Proc-Type:", StringComparison.OrdinalIgnoreCase)
                    && trimmedLine.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
