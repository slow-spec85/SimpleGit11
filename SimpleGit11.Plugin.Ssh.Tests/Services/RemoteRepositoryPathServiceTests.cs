using SimpleGit11.Services.Execution;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class RemoteRepositoryPathServiceTests
{
    [TestMethod]
    public void PosixPaths_AreHandledWithoutLocalPathSemantics()
    {
        RemoteRepositoryPathService paths = new(RepositoryPathStyle.Posix);

        Assert.AreEqual("/srv/repo/.git/config", paths.Combine("/srv/repo", ".git/config"));
        Assert.AreEqual("/srv/repo", paths.GetParent("/srv/repo/file.txt"));
        Assert.AreEqual("file.txt", paths.GetFileName("/srv/repo/file.txt"));
        Assert.AreEqual("/srv/repo/file.txt", paths.Normalize("/srv/temp/../repo/./file.txt"));
    }
}
