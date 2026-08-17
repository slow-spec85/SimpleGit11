using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitConflictMarkerDetectorTests
{
    [TestMethod]
    [DataRow("<<<<<<< HEAD")]
    [DataRow("||||||| parent")]
    [DataRow("=======")]
    [DataRow(">>>>>>> branch")]
    public void IsMarker_KnownMarker_ReturnsTrue(string line)
    {
        Assert.IsTrue(GitConflictMarkerDetector.IsMarker(line));
    }

    [TestMethod]
    public void ContainsMarkers_CrlfContent_ReturnsTrue()
    {
        const string content = "before\r\n<<<<<<< HEAD\r\nours\r\n=======\r\ntheirs\r\n>>>>>>> branch";

        Assert.IsTrue(GitConflictMarkerDetector.ContainsMarkers(content));
    }

    [TestMethod]
    public void IsMarker_MarkerNotAtStart_ReturnsFalse()
    {
        Assert.IsFalse(GitConflictMarkerDetector.IsMarker("  <<<<<<< HEAD"));
    }
}
