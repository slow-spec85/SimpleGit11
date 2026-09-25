using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ChangesViewModeTests
{
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void GetNextFullFileMode_ReturnsOppositeMode(bool currentMode, bool expectedMode)
    {
        Assert.AreEqual(expectedMode, ChangesViewModel.GetNextFullFileMode(currentMode));
    }
}
