using SimpleGit11.Models;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class CommitDialogRequestTests
{
    [TestMethod]
    public void CreateMerge_PreservesPreparedCommitMessage()
    {
        CommitDialogRequest request = CommitDialogRequest.CreateMerge(
            [],
            "Merge branch 'feature'");

        Assert.AreEqual(CommitDialogMode.Merge, request.Mode);
        Assert.AreEqual("Merge branch 'feature'", request.InitialMessage);
    }
}
