using SimpleGit11.Services.Execution;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SshIdentityViewItemTests
{
    [TestMethod]
    public async Task Commands_CopyDisplayedValuesAndShowMatchingPublicKey()
    {
        List<string> copied = [];
        string? shownKey = null;
        SshIdentity identity = new(@"C:\keys\id_ed25519", "ssh-ed25519 AAAA test", "SHA256:test");
        SshIdentityViewItem item = new(
            identity,
            _ => Task.CompletedTask,
            copied.Add,
            key =>
            {
                shownKey = key;
                return Task.CompletedTask;
            });

        item.CopyPathCommand.Execute(null);
        item.CopyFingerprintCommand.Execute(null);
        await item.ShowPublicKeyCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { identity.PrivateKeyPath, identity.Fingerprint },
            copied);
        Assert.AreEqual(identity.PublicKey, shownKey);
    }
}
