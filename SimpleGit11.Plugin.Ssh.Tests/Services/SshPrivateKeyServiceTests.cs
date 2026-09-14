using Renci.SshNet;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshPrivateKeyServiceTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("key passphrase")]
    public async Task GenerateAsync_CreatesPrivateKeyAcceptedBySshNet(string? passphrase)
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
        }
        finally
        {
            File.Delete(path);
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
            File.Delete(encryptedPath);
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
        }
    }
}
