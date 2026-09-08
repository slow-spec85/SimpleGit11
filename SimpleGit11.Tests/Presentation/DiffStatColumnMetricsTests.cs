using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Controls;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class DiffStatColumnMetricsTests
{
    [TestMethod]
    public void Update_PublishesBothMeasuredWidths()
    {
        DiffStatColumnMetrics metrics = new();
        List<string?> changedProperties = [];
        metrics.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        metrics.Update(42.5, 18.25);

        Assert.AreEqual(42.5, metrics.AddedWidth);
        Assert.AreEqual(18.25, metrics.RemovedWidth);
        CollectionAssert.AreEqual(
            new[] { nameof(metrics.AddedWidth), nameof(metrics.RemovedWidth) },
            changedProperties);
    }

    [TestMethod]
    public void Update_WithUnchangedWidths_DoesNotPublishAgain()
    {
        DiffStatColumnMetrics metrics = new();
        int changeCount = 0;
        metrics.PropertyChanged += (_, _) => changeCount++;

        metrics.Update(42, 18);
        changeCount = 0;
        metrics.Update(42, 18);

        Assert.AreEqual(0, changeCount);
    }
}
