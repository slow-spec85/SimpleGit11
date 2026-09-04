using System.Globalization;
using SimpleGit11.Services.Execution;
using SimpleGit11.Plugin.Ssh.Services;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
[TestCategory("SshIntegration")]
public sealed class SshExecutionIntegrationTests
{
    [TestMethod]
    public async Task ConnectAsync_ConfiguredSshHost_RunsGitAndReadsDirectory()
    {
        string? host = Environment.GetEnvironmentVariable("SIMPLEGIT11_SSH_TEST_HOST");
        string? username = Environment.GetEnvironmentVariable("SIMPLEGIT11_SSH_TEST_USERNAME");
        string? expectedHostKey = Environment.GetEnvironmentVariable(
            "SIMPLEGIT11_SSH_TEST_HOST_KEY");
        string? testDirectory = Environment.GetEnvironmentVariable(
            "SIMPLEGIT11_SSH_TEST_DIRECTORY");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(expectedHostKey)
            || string.IsNullOrWhiteSpace(testDirectory))
        {
            Assert.Inconclusive(
                "Set SIMPLEGIT11_SSH_TEST_HOST, SIMPLEGIT11_SSH_TEST_USERNAME, " +
                "SIMPLEGIT11_SSH_TEST_HOST_KEY and SIMPLEGIT11_SSH_TEST_DIRECTORY " +
                "to run the SSH integration test.");
        }

        Dictionary<string, string> settings = new()
        {
            [SshConnectionRequestKeys.Host] = host,
            [SshConnectionRequestKeys.Username] = username,
            [SshConnectionRequestKeys.ExpectedHostKey] = expectedHostKey
        };
        string? port = Environment.GetEnvironmentVariable("SIMPLEGIT11_SSH_TEST_PORT");
        if (!string.IsNullOrWhiteSpace(port))
        {
            settings[SshConnectionRequestKeys.Port] = int.Parse(
                port,
                CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        string? privateKeyPath = Environment.GetEnvironmentVariable(
            "SIMPLEGIT11_SSH_TEST_PRIVATE_KEY");
        if (!string.IsNullOrWhiteSpace(privateKeyPath))
        {
            settings[SshConnectionRequestKeys.PrivateKeyPath] = privateKeyPath;
        }

        Dictionary<string, string> secrets = [];
        AddSecret(secrets, SshConnectionRequestKeys.Password, "SIMPLEGIT11_SSH_TEST_PASSWORD");
        AddSecret(
            secrets,
            SshConnectionRequestKeys.PrivateKeyPassphrase,
            "SIMPLEGIT11_SSH_TEST_PRIVATE_KEY_PASSPHRASE");

        SshExecutionProvider provider = new();
        await using IExecutionRuntime runtime = await provider.ConnectAsync(
            new ExecutionConnectionRequest("integration-test", settings, secrets));
        GitCommandResult gitVersion = await runtime.Git.RunAsync(
            "",
            ["--version"],
            new GitCommandOptions(UseDefaultWorkingDirectory: true));

        Assert.IsTrue(gitVersion.IsSuccess);
        StringAssert.Contains(gitVersion.StandardOutput, "git version");
        Assert.IsTrue(await runtime.Files.DirectoryExistsAsync(testDirectory));
    }

    private static void AddSecret(
        IDictionary<string, string> secrets,
        string requestKey,
        string environmentVariable)
    {
        string? value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrEmpty(value))
        {
            secrets[requestKey] = value;
        }
    }
}
