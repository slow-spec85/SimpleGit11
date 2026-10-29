using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using SimpleGit11.Controls;
using Windows.Foundation;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class CommitBrowserOverlayTests
{
    [TestMethod]
    [DataRow(400, 500, 100, 460, 180, 32)]
    [DataRow(300, 280, 60, 240, 160, 32)]
    public void OverlayClip_HidesOnlyTheButtonAreaWithoutShorteningTheList(
        int width, int height, int x, int y, int buttonWidth, int buttonHeight)
    {
        using CanvasGeometry geometry = CommitBrowserView.CreateCommitListClipGeometry(
            new Size(width, height), new Rect(x, y, buttonWidth, buttonHeight), 4);

        Assert.IsFalse(geometry.FillContainsPoint(new Vector2(x + buttonWidth / 2, y + buttonHeight / 2)));
        Assert.IsTrue(geometry.FillContainsPoint(new Vector2(x - 1, y + buttonHeight / 2)));
        Assert.IsTrue(geometry.FillContainsPoint(new Vector2(x + buttonWidth + 1, y + buttonHeight / 2)));
        Assert.IsTrue(geometry.FillContainsPoint(new Vector2(x + buttonWidth / 2, y - 1)));
        Assert.IsTrue(geometry.FillContainsPoint(new Vector2(x + buttonWidth / 2, height - 1)));
        Assert.IsTrue(geometry.FillContainsPoint(new Vector2(x + 0.1f, y + 0.1f)));
        Assert.IsFalse(geometry.FillContainsPoint(new Vector2(width + 1, height / 2)));
        Assert.AreEqual(new Rect(0, 0, width, height), geometry.ComputeBounds());
    }
}
