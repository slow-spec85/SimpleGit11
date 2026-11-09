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
    [DataRow(0, 16)]
    [DataRow(96, 16)]
    [DataRow(120, 20)]
    [DataRow(144, 24)]
    [DataRow(192, 32)]
    [DataRow(240, 40)]
    public void GetBadgeIconSize_ScalesWithDpi(int dpi, int expectedSize)
    {
        Assert.AreEqual(expectedSize, TaskbarChangesBadge.GetBadgeIconSize((uint)dpi));
    }

    [TestMethod]
    [DataRow(1, 16)]
    [DataRow(33, 16)]
    [DataRow(100, 16)]
    [DataRow(1, 24)]
    [DataRow(33, 24)]
    [DataRow(100, 24)]
    [DataRow(1, 32)]
    [DataRow(33, 32)]
    [DataRow(100, 32)]
    public void RenderBadgePixels_DrawsRoundedBadgeWithCenteredLightText(int count, int iconSize)
    {
        uint[]? pixels = TaskbarChangesBadge.RenderBadgePixels(count, iconSize);

        Assert.IsNotNull(pixels);
        Assert.HasCount(iconSize * iconSize, pixels);
        Assert.AreEqual(0u, pixels[0]);
        Assert.AreEqual(0u, pixels[^1]);
        Assert.Contains(0xff26252du, pixels);
        Assert.IsTrue(pixels.Any(pixel => pixel >> 24 is > 0 and < 255));
        Assert.IsTrue(pixels.Any(pixel => pixel >= 0xffc0c0c0u));
        Assert.AreEqual(count < 10, pixels[iconSize / 2] != 0);

        int[] ink = Enumerable.Range(0, pixels.Length).Where(index => pixels[index] >= 0xff808080u).ToArray();
        int left = ink.Min(index => index % iconSize);
        int right = ink.Max(index => index % iconSize);
        int top = ink[0] / iconSize;
        int bottom = ink[^1] / iconSize;
        Assert.AreEqual((iconSize - 1) / 2.0, (left + right) / 2.0, 0.5);
        Assert.AreEqual((iconSize - 1) / 2.0, (top + bottom) / 2.0, 0.5);
    }
}
