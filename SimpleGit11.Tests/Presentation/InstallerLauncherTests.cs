using SimpleGit11.Presentation.Services;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class InstallerLauncherTests
{
    [TestMethod]
    public void Launch_MissingInstaller_ThrowsBeforeStartingProcess()
    {
        InstallerLauncher launcher = new();

        Assert.ThrowsExactly<FileNotFoundException>(() => launcher.Launch(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.msi")));
    }

    [TestMethod]
    public void Launch_NonMsiFile_ThrowsBeforeStartingProcess()
    {
        using TemporaryDirectory directory = new();
        string path = directory.CreateFile("installer.txt", "fixture");
        InstallerLauncher launcher = new();

        Assert.ThrowsExactly<FileNotFoundException>(() => launcher.Launch(path));
    }
}
