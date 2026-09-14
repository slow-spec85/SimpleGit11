using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitRemoteOperationErrorClassifierTests
{
    [TestMethod]
    [DataRow("git@example.com: Permission denied (publickey).")]
    [DataRow("ERROR: Your SSH key has expired.")]
    [DataRow("The SSH key is expired")]
    [DataRow("fatal: Authentication failed")]
    [DataRow("fatal: could not read Username")]
    public void Classify_AuthenticationOutput_ReturnsAuthentication(string output)
    {
        Assert.AreEqual(
            GitRemoteOperationErrorKind.Authentication,
            GitRemoteOperationErrorClassifier.Classify(output));
    }

    [TestMethod]
    [DataRow("Git Credential Manager failed. TLS certificate failure")]
    [DataRow("Git Credential Manager exited with code 1 without returning credentials.")]
    [DataRow("Git Credential Manager did not return both a username and a password.")]
    public void Classify_CredentialManagerOutput_ReturnsCredentialManager(string output)
    {
        Assert.AreEqual(
            GitRemoteOperationErrorKind.CredentialManager,
            GitRemoteOperationErrorClassifier.Classify(output));
    }
}
