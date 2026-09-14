using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SettingsSshCommandTests
{
    [TestMethod]
    public void CreateRepositorySshCommand_UsesAbsoluteKeyAndIdentitiesOnly()
    {
        string command = SettingsViewModel.CreateRepositorySshCommand(
            @"C:\Users\test user\.ssh\id_ed25519_gitlab_com");

        Assert.AreEqual(
            "ssh -i \"C:/Users/test user/.ssh/id_ed25519_gitlab_com\" -o IdentitiesOnly=yes",
            command);
    }

    [TestMethod]
    public void GetRepositorySshIdentityPath_ReadsGeneratedCommand()
    {
        string? path = SettingsViewModel.GetRepositorySshIdentityPath(
            "ssh -i \"/home/test user/.ssh/id_ed25519_gitlab_com\" -o IdentitiesOnly=yes");

        Assert.AreEqual("/home/test user/.ssh/id_ed25519_gitlab_com", path);
    }
}
