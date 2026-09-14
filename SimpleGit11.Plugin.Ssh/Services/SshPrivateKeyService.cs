using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Security;

namespace SimpleGit11.Plugin.Ssh.Services;

internal sealed class SshPrivateKeyService : ISshPrivateKeyService
{
    private const int KeySize = 3072;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public async Task GenerateAsync(
        string path,
        string? passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string privateKey = await Task.Run(
            () => CreatePrivateKey(passphrase),
            cancellationToken);
        await File.WriteAllTextAsync(
            fullPath,
            privateKey,
            Utf8WithoutBom,
            cancellationToken);
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

    private static string CreatePrivateKey(string? passphrase)
    {
        using RSA rsa = RSA.Create();
        rsa.KeySize = KeySize;
        if (string.IsNullOrEmpty(passphrase))
        {
            return rsa.ExportPkcs8PrivateKeyPem();
        }

        PbeParameters encryption = new(
            PbeEncryptionAlgorithm.Aes256Cbc,
            HashAlgorithmName.SHA256,
            100_000);
        return rsa.ExportEncryptedPkcs8PrivateKeyPem(passphrase, encryption);
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
