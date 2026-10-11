using System.Collections.Specialized;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ChangesFileFilterTests
{
    [TestMethod]
    [DataRow("HOME", "src/Home.cs")]
    [DataRow(" src\\ ", "src/Home.cs")]
    [DataRow("readme", "docs/Readme.md")]
    [DataRow("missing", "")]
    public void Search_MatchesFilePathsIgnoringCaseAndNormalizingSeparators(string query, string expectedPath)
    {
        ChangesViewModel model = CreateModel();
        LoadChanges(model,
            new("src/Home.cs", "Modified", state: GitChangeState.Unstaged),
            new("docs/Readme.md", "Modified", state: GitChangeState.Staged));

        model.FileSearchText = query;

        CollectionAssert.AreEqual(expectedPath.Length == 0 ? Array.Empty<string>() : [expectedPath],
            model.VisibleChanges.Select(change => change.Path).ToArray());
        Assert.IsTrue(model.IsFileFilterApplied);
        Assert.HasCount(2, model.AllChanges);
        Assert.IsTrue(model.HasChanges);
        Assert.IsTrue(model.StageAllCommand.CanExecute(null));
        Assert.IsTrue(model.UnstageAllCommand.CanExecute(null));
    }

    [TestMethod]
    public void ClearingSearch_RestoresAllStatesAndPreservesSummaryAndCollection()
    {
        ChangesViewModel model = CreateModel();
        LoadChanges(model,
            new("src/Home.cs", "Modified", new DiffStat(2, 1), GitChangeState.Staged),
            new("src/Home.cs", "Modified", new DiffStat(3, 2), GitChangeState.Unstaged),
            new("docs/Readme.md", "Conflict", state: GitChangeState.Conflicted));
        object collection = model.VisibleChanges;
        string title = model.ChangedFilesTitle;

        model.FileSearchText = "home";
        Assert.HasCount(2, model.VisibleChanges);
        Assert.AreEqual(title, model.ChangedFilesTitle);
        Assert.AreEqual(5, model.ChangedFilesStat.AddedLines);
        Assert.AreEqual(3, model.ChangedFilesStat.RemovedLines);

        model.FileSearchText = "   ";

        Assert.IsFalse(model.IsFileFilterApplied);
        Assert.AreSame(collection, model.VisibleChanges);
        CollectionAssert.AreEqual(model.AllChanges.ToArray(), model.VisibleChanges.ToArray());
    }

    [TestMethod]
    public void ChangingSearch_KeepsMatchingRowsAndDropsHiddenSelection()
    {
        ChangesViewModel model = CreateModel();
        GitChangedFile first = new("src/First.cs", "Modified", state: GitChangeState.Unstaged);
        GitChangedFile second = new("src/Second.cs", "Modified", state: GitChangeState.Unstaged);
        LoadChanges(model, first, second);
        model.SetSelectedChanges([first, second], second);
        List<NotifyCollectionChangedAction> actions = [];
        model.VisibleChanges.CollectionChanged += (_, args) => actions.Add(args.Action);

        model.FileSearchText = "First";

        CollectionAssert.AreEqual(new[] { first }, model.VisibleChanges.ToArray());
        CollectionAssert.AreEqual(new[] { first }, model.SelectedChanges.ToArray());
        Assert.AreSame(first, model.SelectedChange);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Remove }, actions);

        // Collection updates can raise a selection event before all hidden rows are removed.
        model.SetSelectedChanges([first, second], second);
        CollectionAssert.AreEqual(new[] { first }, model.SelectedChanges.ToArray());
        Assert.AreSame(first, model.SelectedChange);

        model.FileSearchText = "missing";

        Assert.IsEmpty(model.SelectedChanges);
        Assert.IsNull(model.SelectedChange);
        Assert.IsFalse(model.CanStageSelected);
        Assert.IsFalse(model.CanDiscardSelected);
        Assert.IsTrue(model.HasDiffEmptyState);
    }

    [TestMethod]
    public void StatusReplacement_ReappliesSearchAndRepositoryChangeResetsIt()
    {
        ChangesViewModel model = CreateModel();
        LoadChanges(model, new GitChangedFile("src/Old.cs", "Modified", state: GitChangeState.Unstaged));
        model.FileSearchText = "src/";

        LoadChanges(model,
            new("docs/Readme.md", "Modified", state: GitChangeState.Staged),
            new("src/New.cs", "Modified", state: GitChangeState.Unstaged));

        Assert.AreEqual("src/New.cs", model.VisibleChanges.Single().Path);
        model.Receive(new RepositoryChangedMessage(null));
        Assert.AreEqual("", model.FileSearchText);
        Assert.IsFalse(model.IsFileFilterApplied);
        Assert.IsEmpty(model.VisibleChanges);
        Assert.IsEmpty(model.AllChanges);
    }

    private static void LoadChanges(ChangesViewModel model, params GitChangedFile[] changes)
    {
        model.StagedChanges.Clear();
        model.UnstagedChanges.Clear();
        model.ConflictedChanges.Clear();
        foreach (GitChangedFile change in changes)
        {
            switch (change.State)
            {
                case GitChangeState.Staged: model.StagedChanges.Add(change); break;
                case GitChangeState.Conflicted: model.ConflictedChanges.Add(change); break;
                default: model.UnstagedChanges.Add(change); break;
            }
        }

        typeof(ChangesViewModel).GetMethod("ReplaceAllChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, null);
    }

    private static ChangesViewModel CreateModel()
    {
        StrongReferenceMessenger messenger = new();
        ILocalizationService localization = Stub.Create<ILocalizationService>((method, args) => method switch
        {
            "GetString" => args![0],
            "get_CurrentLanguage" => AppLanguage.English,
            _ => throw new NotSupportedException(method)
        });
        IGitService git = Stub.Create<IGitService>();
        ISettingsService settings = Stub.Create<ISettingsService>((_, _) => new AppSettings());
        IAsyncCommandExecutor executor = Stub.Create<IAsyncCommandExecutor>((_, args) => ((Func<Task>)args![0]!)());
        MainWindowViewModel window = new(
            Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
            localization, git, null!, new TestProductInfoService(), messenger, settings);
        ConflictEditorViewModel conflictEditor = new(null!, git, localization, messenger, executor);
        ChangesViewModel model = new(window, git, localization, null!, null!, settings, null!,
            conflictEditor, messenger, executor);
        typeof(ChangesViewModel).GetField("_suppressSelectionLoad", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, true);
        return model;
    }
}
