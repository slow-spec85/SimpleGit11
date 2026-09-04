using System.Text;
using SimpleGit11.Services.Execution;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class RemoteCommandComposerTests
{
    [TestMethod]
    public void ComposeGit_Posix_QuotesWorkingDirectoryArgumentsAndEnvironment()
    {
        string command = RemoteCommandComposer.ComposeGit(
            RepositoryPathStyle.Posix,
            "/srv/team's repo",
            ["commit", "-m", "it's ready"],
            new Dictionary<string, string> { ["GIT_EDITOR"] = "value with spaces" });

        Assert.AreEqual(
            "cd -- '/srv/team'\"'\"'s repo' && GIT_EDITOR='value with spaces' git 'commit' '-m' 'it'\"'\"'s ready'",
            command);
    }

    [TestMethod]
    public void ComposeGit_Windows_ProducesEncodedPowerShellCommand()
    {
        string command = RemoteCommandComposer.ComposeGit(
            RepositoryPathStyle.Windows,
            @"C:\Source\O'Brien",
            ["status", "--porcelain=v2"],
            null);

        string prefix = "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand ";
        Assert.IsTrue(command.StartsWith(prefix, StringComparison.Ordinal));
        string script = Encoding.Unicode.GetString(Convert.FromBase64String(command[prefix.Length..]));
        Assert.AreEqual(
            "$ErrorActionPreference='Stop'; Set-Location -LiteralPath 'C:\\Source\\O''Brien';& git 'status' '--porcelain=v2'; exit $LASTEXITCODE",
            script);
    }

    [TestMethod]
    public void ComposeGit_InvalidEnvironmentName_Throws()
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandComposer.ComposeGit(
            RepositoryPathStyle.Posix,
            "/repo",
            ["status"],
            new Dictionary<string, string> { ["BAD-NAME"] = "value" }));
    }

    [TestMethod]
    public void ComposeGit_DefaultWorkingDirectory_DoesNotUseLocalPath()
    {
        string command = RemoteCommandComposer.ComposeGit(
            RepositoryPathStyle.Posix,
            @"D:\Source\SimpleGit11",
            ["config", "--global", "user.name"],
            null,
            useDefaultWorkingDirectory: true);

        Assert.AreEqual("git 'config' '--global' 'user.name'", command);
    }
}
