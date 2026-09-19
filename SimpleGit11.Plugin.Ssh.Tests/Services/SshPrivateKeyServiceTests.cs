using Renci.SshNet;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshPrivateKeyServiceTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("key passphrase")]
    public async Task GenerateAsync_CreatesMatchingPrivateAndOpenSshPublicKeys(string? passphrase)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"simplegit11-test-{Guid.NewGuid():N}.pem");
        try
        {
            SshPrivateKeyService privateKeyService = new();

            await privateKeyService.GenerateAsync(path, passphrase);

            using PrivateKeyFile privateKey = passphrase is null
                ? new PrivateKeyFile(path)
                : new PrivateKeyFile(path, passphrase);
            Assert.IsNotEmpty(privateKey.HostKeyAlgorithms);
            Assert.IsTrue(privateKey.HostKeyAlgorithms.Any(
                algorithm => algorithm.Name == "ssh-rsa"));

            string publicKey = await File.ReadAllTextAsync(path + ".pub");
            string[] fields = publicKey.TrimEnd().Split(' ');
            Assert.HasCount(2, fields);
            Assert.AreEqual("ssh-rsa", fields[0]);
            CollectionAssert.AreEqual(
                privateKey.HostKeyAlgorithms.First(algorithm => algorithm.Name == "ssh-rsa").Data,
                Convert.FromBase64String(fields[1]));
            Assert.IsTrue(publicKey.EndsWith(Environment.NewLine, StringComparison.Ordinal));
            byte[] publicKeyBytes = await File.ReadAllBytesAsync(path + ".pub");
            Assert.AreEqual((byte)'s', publicKeyBytes[0], "The public key must not contain a UTF-8 BOM.");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".pub");
        }
    }

    [TestMethod]
    public async Task RequiresPassphraseAsync_ReportsWhetherGeneratedKeyIsEncrypted()
    {
        string plainPath = Path.Combine(Path.GetTempPath(), $"simplegit11-{Guid.NewGuid():N}");
        string encryptedPath = Path.Combine(Path.GetTempPath(), $"simplegit11-{Guid.NewGuid():N}");
        try
        {
            SshPrivateKeyService privateKeyService = new();
            await privateKeyService.GenerateAsync(plainPath, null);
            await privateKeyService.GenerateAsync(encryptedPath, "key passphrase");

            Assert.IsFalse(await privateKeyService.RequiresPassphraseAsync(plainPath));
            Assert.IsTrue(await privateKeyService.RequiresPassphraseAsync(encryptedPath));
        }
        finally
        {
            File.Delete(plainPath);
            File.Delete(plainPath + ".pub");
            File.Delete(encryptedPath);
            File.Delete(encryptedPath + ".pub");
        }
    }

    [TestMethod]
    public async Task CanOpenAsync_ValidatesEncryptedKeyPassphrase()
    {
        string path = Path.Combine(Path.GetTempPath(), $"simplegit11-{Guid.NewGuid():N}");
        try
        {
            SshPrivateKeyService privateKeyService = new();
            await privateKeyService.GenerateAsync(path, "correct passphrase");

            Assert.IsTrue(await privateKeyService.CanOpenAsync(path, "correct passphrase"));
            Assert.IsFalse(await privateKeyService.CanOpenAsync(path, "wrong passphrase"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".pub");
        }
    }

    [TestMethod]
    public async Task GetAuthorizedKeyAsync_ReturnsOpenSshPublicKey()
    {
        string path = Path.Combine(Path.GetTempPath(), $"simplegit11-{Guid.NewGuid():N}");
        try
        {
            SshPrivateKeyService privateKeyService = new();
            await privateKeyService.GenerateAsync(path, "key passphrase");

            string authorizedKey = await privateKeyService.GetAuthorizedKeyAsync(
                path,
                "key passphrase");

            string[] parts = authorizedKey.Split(' ', 2);
            Assert.AreEqual("ssh-rsa", parts[0]);
            Assert.IsNotEmpty(Convert.FromBase64String(parts[1]));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".pub");
        }
    }
}
