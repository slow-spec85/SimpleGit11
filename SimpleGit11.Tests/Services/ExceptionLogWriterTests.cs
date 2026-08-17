using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class ExceptionLogWriterTests
{
    [TestMethod]
    public void GetLogFilePath_UsesApplicationLogsDirectoryInLocalApplicationData()
    {
        const string fileName = "test.log";
        string expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpleGit11",
            "Logs",
            fileName);

        string actualPath = ExceptionLogWriter.GetLogFilePath(fileName);

        Assert.AreEqual(expectedPath, actualPath);
    }

    [TestMethod]
    public void GetLogFilePath_RejectsFileNameContainingPath()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ExceptionLogWriter.GetLogFilePath(Path.Combine("nested", "test.log")));
    }
}
