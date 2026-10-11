using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;
using SimpleGit11.Services;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class HistoryEditSessionTests
{
    private static readonly ILocalizationService Strings = Stub.Create<ILocalizationService>((_, args) => args![0] is "HistoryEditMergedInto" ? "{0} {1}" : args[0]);
    private static GitCommit Commit(string hash, string message) => new(hash, hash, "Author", "a@example.invalid", null, message.Split('\n')[0], message);
    private static HistoryEditSession Session() => new([Commit("ccccccc", "C"), Commit("bbbbbbb", "B"), Commit("aaaaaaa", "A")]);
    private const string Plan = "pick aaaaaaa A\npick bbbbbbb B\npick ccccccc C\n# help\n";

    [TestMethod]
    [DataRow("pick\npick bbbbbbb B\npick ccccccc C\n")]
    [DataRow("exec echo test\npick bbbbbbb B\npick ccccccc C\n")]
    [DataRow("merge -C aaaaaaa label\npick bbbbbbb B\npick ccccccc C\n")]
    public void DraftRejectsMalformedOrUnexpectedInstructions(string plan)
    {
        Assert.Throws<InvalidOperationException>(() => Session().BuildPlan(plan));
    }

    [TestMethod]
    [DataRow("\r")]
    [DataRow("\u0085")]
    [DataRow("\u2028")]
    [DataRow("\u2029")]
    public void LineSeparatorsInsideGitSubjectAreNotParsedAsTodoCommands(string separator)
    {
        HistoryEditSession draft = Session();
        draft.SetMessage("bbbbbbb", "New title\n\nBody");
        string plan = $"pick aaaaaaa A\npick\tbbbbbbb B{separator}{separator}Old body{separator}# old editor hint\npick ccccccc C\n# help\n";
        Assert.AreEqual("pick aaaaaaa\nreword bbbbbbb\npick ccccccc\n", draft.BuildPlan(plan));
    }

    [TestMethod]
    public void WindowsTodoLineEndingsAndAbbreviatedPickAreSupported()
    {
        HistoryEditSession draft = Session();
        draft.SetDeleted("bbbbbbb", true);
        Assert.AreEqual("pick aaaaaaa\ndrop bbbbbbb\npick ccccccc\n",
            draft.BuildPlan("p aaaaaaa A\r\np\tbbbbbbb B\r\np ccccccc C\r\n# help\r\n"));
    }

    [TestMethod]
    [DataRow("Title\n\nBody")]
    [DataRow("Title\r\n\r\nBody")]
    [DataRow("Title\rBody")]
    public void RowTitleDoesNotIncludeMessageBody(string message)
    {
        HistoryEditSession draft = new([Commit("aaaaaaa", message)]);
        Assert.AreEqual("Title", draft.CreateRows(Strings).Single().DisplayTitle);
        draft.SetMessage("aaaaaaa", "Edited\n\nNew body");
        CommitBrowserRowViewItem row = draft.CreateRows(Strings).Single();
        Assert.AreEqual("Edited", row.DisplayTitle);
        Assert.AreEqual("\uE70F", row.EditGlyph);
        Assert.IsTrue(row.HasChangedIndicator);
        Assert.IsFalse(row.HasDeletedIndicator);
    }

    [TestMethod]
    public void IndicatorIconsMatchDeleteCombineAndVisualMoveDirections()
    {
        HistoryEditSession draft = Session();
        draft.Move("bbbbbbb", -1);
        Assert.AreEqual("\uE74A", draft.CreateRows(Strings).Single(row => row.Commit.Hash == "bbbbbbb").EditGlyph);
        Assert.AreEqual("\uE74B", draft.CreateRows(Strings).Single(row => row.Commit.Hash == "ccccccc").EditGlyph);
        draft.Undo();
        draft.Merge("bbbbbbb", "Combined");
        Assert.IsTrue(draft.CreateRows(Strings).Where(row => row.Commit.Hash != "ccccccc").All(row => row.EditGlyph == "\uE71B" && row.HasChangedIndicator));
        draft.SetDeleted("aaaaaaa", true);
        Assert.IsTrue(draft.CreateRows(Strings).Where(row => row.Commit.Hash != "ccccccc").All(row => row.EditGlyph == "\uE74D" && row.HasDeletedIndicator && !row.HasChangedIndicator));
        draft.SetDeleted("aaaaaaa", false);
        Assert.IsTrue(draft.CreateRows(Strings).All(row => !row.HasDeletedIndicator));
    }

    [TestMethod]
    public void DeleteKeepsRowAndRestoreAndUndoPreserveOriginalHistory()
    {
        HistoryEditSession draft = Session();
        draft.SetDeleted("bbbbbbb", true);
        Assert.AreEqual(3, draft.CreateRows(Strings).Count());
        Assert.AreEqual("HistoryEditDeleted", draft.CreateRows(Strings).Single(row => row.Commit.Hash == "bbbbbbb").EditIndicator);
        Assert.AreEqual("pick aaaaaaa\ndrop bbbbbbb\npick ccccccc\n", draft.BuildPlan(Plan));
        Assert.IsTrue(draft.CreateRows(Strings).Single(row => row.Commit.Hash == "ccccccc").IsDraftContentUnavailable);
        draft.SetDeleted("bbbbbbb", false);
        Assert.IsFalse(draft.HasChanges);
        draft.Undo();
        Assert.IsTrue(draft.IsDeleted("bbbbbbb"));
        draft.Undo();
        Assert.IsFalse(draft.HasChanges);
        Assert.IsFalse(draft.CanUndo);
    }

    [TestMethod]
    public void MergeSkipsDeletedCommitAndRetainsGhostAndOriginalMessages()
    {
        HistoryEditSession draft = Session();
        draft.SetDeleted("bbbbbbb", true);
        Assert.AreEqual("aaaaaaa", draft.Previous("ccccccc")!.Hash);
        draft.Merge("ccccccc", "Combined\n\nBody");
        CommitBrowserRowViewItem[] rows = draft.CreateRows(Strings).ToArray();
        Assert.AreEqual(3, rows.Length);
        Assert.AreEqual("C", draft.Original[0].Message);
        Assert.AreEqual("HistoryEditMerged", rows.Single(row => row.Commit.Hash == "ccccccc").EditIndicator);
        Assert.IsFalse(draft.IsActive("ccccccc"));
        Assert.AreEqual("reword aaaaaaa\nfixup ccccccc\ndrop bbbbbbb\n", draft.BuildPlan(Plan));
        Assert.AreEqual("Combined\n\nBody\n", draft.PreparedMessages["aaaaaaa"]);
        draft.Undo();
        Assert.IsTrue(draft.IsActive("ccccccc"));
        Assert.AreEqual("A", draft.Message("aaaaaaa"));
    }

    [TestMethod]
    public void MovingCombinedGroupPreservesPatchOrderAndUndoRestoresBothGroups()
    {
        HistoryEditSession draft = Session();
        draft.Merge("bbbbbbb");
        draft.Move("aaaaaaa", -1);
        Assert.AreEqual("pick ccccccc\npick aaaaaaa\nfixup bbbbbbb\n", draft.BuildPlan(Plan));
        CollectionAssert.AreEqual(new[] { "bbbbbbb", "aaaaaaa", "ccccccc" }, draft.CreateRows(Strings).Select(row => row.Commit.Hash).ToArray());
        draft.Undo();
        Assert.AreEqual("pick aaaaaaa\nfixup bbbbbbb\npick ccccccc\n", draft.BuildPlan(Plan));
        draft.Undo();
        Assert.IsFalse(draft.HasChanges);
    }

    [TestMethod]
    public void MessageOnlyEditKeepsContentAvailableButDeletionOfGroupHidesAllMembers()
    {
        HistoryEditSession draft = Session();
        draft.SetMessage("aaaaaaa", "New title\r\rBody");
        Assert.IsFalse(draft.CreateRows(Strings).Single(row => row.Commit.Hash == "aaaaaaa").IsDraftContentUnavailable);
        Assert.AreEqual("New title\n\nBody\n", draft.PreparedMessages["aaaaaaa"]);
        draft.Merge("bbbbbbb");
        draft.SetDeleted("aaaaaaa", true);
        Assert.IsTrue(draft.CreateRows(Strings).Where(row => row.Commit.Hash != "ccccccc").All(row => row.IsDraftContentUnavailable));
        Assert.AreEqual("HistoryEditDeletedHint", draft.CreateRows(Strings).Single(row => row.Commit.Hash == "bbbbbbb").EditTooltip);
        Assert.AreEqual("drop aaaaaaa\ndrop bbbbbbb\npick ccccccc\n", draft.BuildPlan(Plan));
        Assert.AreEqual(0, draft.PreparedMessages.Count);
    }

    [TestMethod]
    public void ParentLineChangeIsTrackedEvenWhenTheVisibleOrderStaysTheSame()
    {
        static GitCommit Node(string hash, params string[] parents) => new(hash, hash, "", "", null, hash, hash, parentHashes: parents);
        HistoryEditSession draft = new([Node("eeeeeee", "ddddddd"), Node("ddddddd", "bbbbbbb", "ccccccc"),
            Node("bbbbbbb", "aaaaaaa"), Node("ccccccc", "aaaaaaa")]);
        string[] before = draft.CreateRows(Strings).Select(row => row.Commit.Hash).ToArray();
        draft.Move("bbbbbbb", 1, 2);
        CollectionAssert.AreEqual(before, draft.CreateRows(Strings).Select(row => row.Commit.Hash).ToArray());
        Assert.IsTrue(draft.HasChanges);
        CommitBrowserRowViewItem moved = draft.CreateRows(Strings).Single(row => row.Commit.Hash == "bbbbbbb");
        Assert.AreEqual("HistoryEditMoved", moved.EditIndicator);
        Assert.AreEqual("\uE74B", moved.EditGlyph);
        Assert.IsTrue(moved.IsDraftContentUnavailable);
        draft.Undo();
        Assert.IsFalse(draft.HasChanges);
        Assert.IsTrue(draft.CreateRows(Strings).All(row => !row.HasEditIndicator));
    }

    [TestMethod]
    [DataRow("pick aaaaaaa A\npick bbbbbbb B\n")]
    [DataRow("pick aaaaaaa A\npick bbbbbbb B\npick ddddddd Different\n")]
    [DataRow("pick aaaaaaa A\npick bbbbbbb B\nexec echo unexpected\n")]
    public void PlanFromDifferentRangeIsRejected(string plan)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => Session().BuildPlan(plan));
    }

    [TestMethod]
    public void BoundariesAndDeletedRowsCannotBeMovedOrUsedAsMergeSource()
    {
        HistoryEditSession draft = Session();
        Assert.IsNull(draft.Previous("aaaaaaa"));
        Assert.IsFalse(draft.CanMove("ccccccc", -1));
        Assert.IsFalse(draft.CanMove("aaaaaaa", 1));
        draft.SetDeleted("bbbbbbb", true);
        Assert.IsFalse(draft.CanMove("bbbbbbb", -1));
        draft.Move("ccccccc", 1);
        Assert.AreEqual("pick ccccccc\ndrop bbbbbbb\npick aaaaaaa\n", draft.BuildPlan(Plan));
        Assert.IsTrue(draft.CreateRows(Strings).Single(row => row.Commit.Hash == "ccccccc").IsDraftContentUnavailable);
    }
}
