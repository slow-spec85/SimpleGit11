using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Presentation.Services;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class CredentialManagerLauncherTests
{
    [TestMethod]
    public void CreateStartInfo_OpensWindowsCredentialManager()
    {
        ProcessStartInfo startInfo = CredentialManagerLauncher.CreateStartInfo();

        Assert.AreEqual("control.exe", startInfo.FileName);
        Assert.IsTrue(startInfo.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { "/name", "Microsoft.CredentialManager" },
            startInfo.ArgumentList.ToArray());
    }
}
