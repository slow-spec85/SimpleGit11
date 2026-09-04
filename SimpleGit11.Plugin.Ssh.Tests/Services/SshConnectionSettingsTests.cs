using SimpleGit11.Services.Execution;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshConnectionSettingsTests
{
    [TestMethod]
    public void FromRequest_PasswordAuthentication_ReadsAndValidatesValues()
    {
        ExecutionConnectionRequest request = new(
            "profile",
            new Dictionary<string, string>
            {
                [SshConnectionRequestKeys.Host] = " server.example ",
                [SshConnectionRequestKeys.Port] = "2222",
                [SshConnectionRequestKeys.Username] = "git",
                [SshConnectionRequestKeys.ExpectedHostKey] = "SHA256:fingerprint"
            },
            new Dictionary<string, string>
            {
                [SshConnectionRequestKeys.Password] = "secret"
            });

        SshConnectionSettings settings = SshConnectionSettings.FromRequest(request);

        Assert.AreEqual("server.example", settings.Host);
        Assert.AreEqual(2222, settings.Port);
        Assert.AreEqual("git", settings.Username);
        Assert.AreEqual("secret", settings.Password);
        Assert.AreEqual("SHA256:fingerprint", settings.ExpectedHostKey);
    }

    [TestMethod]
    public void FromRequest_MissingAuthentication_Throws()
    {
        ExecutionConnectionRequest request = new(
            null,
            new Dictionary<string, string>
            {
                [SshConnectionRequestKeys.Host] = "server.example",
                [SshConnectionRequestKeys.Username] = "git"
            });

        Assert.Throws<ArgumentException>(() => SshConnectionSettings.FromRequest(request));
    }

    [TestMethod]
    public void FromRequest_InvalidPort_Throws()
    {
        ExecutionConnectionRequest request = new(
            null,
            new Dictionary<string, string>
            {
                [SshConnectionRequestKeys.Host] = "server.example",
                [SshConnectionRequestKeys.Port] = "70000",
                [SshConnectionRequestKeys.Username] = "git"
            },
            new Dictionary<string, string>
            {
                [SshConnectionRequestKeys.Password] = "secret"
            });

        Assert.Throws<ArgumentException>(() => SshConnectionSettings.FromRequest(request));
    }
}
