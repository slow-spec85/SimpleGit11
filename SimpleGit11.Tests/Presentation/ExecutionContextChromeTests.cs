using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class ExecutionContextChromeTests
{
    [TestMethod]
    public void LocalContext_HidesHostAndUsesApplicationNameForWindowTitle()
    {
        TestExecutionContextService contexts = new(
            new InMemoryRepositoryFileSystem(),
            isLocal: true);

        (string titleBarText, string windowTitle) = MainWindow.ResolveExecutionContextTitles(
            contexts.Current,
            "SimpleGit11");

        Assert.AreEqual(string.Empty, titleBarText);
        Assert.AreEqual("SimpleGit11", windowTitle);
    }

    [TestMethod]
    public void RemoteContext_UsesHostForTitleBarAndWindowTitle()
    {
        TestExecutionContextService contexts = new(new InMemoryRepositoryFileSystem());

        (string titleBarText, string windowTitle) = MainWindow.ResolveExecutionContextTitles(
            contexts.Current,
            "SimpleGit11");

        Assert.AreEqual("test-server", titleBarText);
        Assert.AreEqual("test-server", windowTitle);
    }
}
