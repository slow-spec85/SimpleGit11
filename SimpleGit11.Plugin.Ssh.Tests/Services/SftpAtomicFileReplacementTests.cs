using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SftpAtomicFileReplacementTests
{
    [TestMethod]
    public void ReplaceWithoutPosixExtension_ExistingFile_ReplacesAndRemovesBackup()
    {
        Dictionary<string, string> files = new()
        {
            ["/repo/.gitignore.tmp"] = "new",
            ["/repo/.gitignore"] = "old"
        };

        SftpAtomicFileReplacement.ReplaceWithoutPosixExtension(
            "/repo/.gitignore.tmp",
            "/repo/.gitignore",
            files.ContainsKey,
            (source, destination) =>
            {
                files[destination] = files[source];
                files.Remove(source);
            },
            path => files.Remove(path));

        Assert.AreEqual("new", files["/repo/.gitignore"]);
        Assert.HasCount(1, files);
    }

    [TestMethod]
    public void ReplaceWithoutPosixExtension_RenameFailure_RestoresOriginalFile()
    {
        Dictionary<string, string> files = new()
        {
            ["/repo/.gitignore.tmp"] = "new",
            ["/repo/.gitignore"] = "old"
        };
        int renameCount = 0;

        Assert.Throws<IOException>(() =>
            SftpAtomicFileReplacement.ReplaceWithoutPosixExtension(
                "/repo/.gitignore.tmp",
                "/repo/.gitignore",
                files.ContainsKey,
                (source, destination) =>
                {
                    renameCount++;
                    if (renameCount == 2)
                    {
                        throw new IOException("rename failed");
                    }

                    files[destination] = files[source];
                    files.Remove(source);
                },
                path => files.Remove(path)));

        Assert.AreEqual("old", files["/repo/.gitignore"]);
        Assert.AreEqual("new", files["/repo/.gitignore.tmp"]);
    }
}
