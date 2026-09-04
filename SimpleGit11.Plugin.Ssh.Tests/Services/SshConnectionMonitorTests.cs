using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshConnectionMonitorTests
{
    [TestMethod]
    public void Report_MultipleFailures_RaisesConnectionLostOnce()
    {
        SshConnectionMonitor monitor = new();
        List<Exception> failures = [];
        monitor.ConnectionLost += (_, exception) => failures.Add(exception);

        monitor.Report(new IOException("first"));
        monitor.Report(new IOException("second"));

        Assert.HasCount(1, failures);
        Assert.AreEqual("first", failures[0].Message);
    }
}
