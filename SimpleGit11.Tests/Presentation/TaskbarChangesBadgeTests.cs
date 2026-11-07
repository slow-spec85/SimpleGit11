using SimpleGit11.Presentation.Navigation;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class TaskbarChangesBadgeTests
{
    [TestMethod]
    public void CombineWorktreeCounts_CountsEachWorktreeOnceWhenWindowsReportAtDifferentTimes()
    {
        long count = TaskbarChangesBadge.CombineWorktreeCounts([(1, 2), (1, 3), (2, 7), (3, 0)]);

        Assert.AreEqual(10L, count);
        Assert.AreEqual(0L, TaskbarChangesBadge.CombineWorktreeCounts([]));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    [DataRow(100)]
    public void RenderBadgePixels_DrawsRoundedGrayBadgeWithLightText(int count)
    {
        uint[]? pixels = TaskbarChangesBadge.RenderBadgePixels(count);

        Assert.IsNotNull(pixels);
        Assert.HasCount(16 * 16, pixels);
        Assert.AreEqual(0u, pixels[0]);
        Assert.AreEqual(0u, pixels[^1]);
        Assert.Contains(0xff26252du, pixels);
        Assert.IsTrue(pixels.Any(pixel => pixel >> 24 is > 0 and < 255));
        Assert.IsTrue(pixels.Any(pixel => pixel >= 0xffc0c0c0u));
        Assert.AreEqual(count < 10, pixels[8] != 0);
    }
}
