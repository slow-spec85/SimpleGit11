using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class ExecutionContextChromeTests
{
    [TestMethod]
    public void LocalContext_HidesHostAndUsesRepositoryNameForWindowTitle()
    {
        TestExecutionContextService contexts = new(
            new InMemoryRepositoryFileSystem(),
            isLocal: true);

        (string titleBarText, string windowTitle) = MainWindow.ResolveExecutionContextTitles(
            contexts.Current,
            "SimpleGit11",
            "my-repository");

        Assert.AreEqual(string.Empty, titleBarText);
        Assert.AreEqual("my-repository", windowTitle);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public void LocalContext_WithoutRepositoryName_UsesApplicationNameForWindowTitle(string? repositoryName)
    {
        TestExecutionContextService contexts = new(new InMemoryRepositoryFileSystem(), isLocal: true);

        (string titleBarText, string windowTitle) = MainWindow.ResolveExecutionContextTitles(
            contexts.Current,
            "SimpleGit11",
            repositoryName);

        Assert.AreEqual(string.Empty, titleBarText);
        Assert.AreEqual("SimpleGit11", windowTitle);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("my-repository")]
    public void RemoteContext_UsesHostForTitleBarAndWindowTitle(string? repositoryName)
    {
        TestExecutionContextService contexts = new(new InMemoryRepositoryFileSystem());

        (string titleBarText, string windowTitle) = MainWindow.ResolveExecutionContextTitles(
            contexts.Current,
            "SimpleGit11",
            repositoryName);

        Assert.AreEqual("test-server", titleBarText);
        Assert.AreEqual("test-server", windowTitle);
    }
}
