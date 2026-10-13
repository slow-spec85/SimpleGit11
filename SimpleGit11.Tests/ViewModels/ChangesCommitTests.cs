using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ChangesCommitTests
{
    [TestMethod]
    public async Task Commit_EmptyStage_StagesAllFilesBeforePreparationAndDialogDespiteFilter()
    {
        Fixture fixture = new();
        fixture.Model.FileSearchText = "first";

        await fixture.Model.OpenCommitDialogCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "stage", "prepare", "dialog", "commit" }, fixture.Calls);
        CollectionAssert.AreEqual(new[] { "first.txt", "second.txt" }, fixture.CommittedPaths);
        Assert.IsTrue(fixture.Request!.ChangedFiles.All(file => file.IsStaged));
    }

    [TestMethod]
    public async Task Commit_ExistingStage_PreservesPartiallyStagedFileAndOtherUnstagedFiles()
    {
        Fixture fixture = new(hasStagedChanges: true);

        await fixture.Model.OpenCommitDialogCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "prepare", "dialog", "commit" }, fixture.Calls);
        CollectionAssert.AreEqual(new[] { "first.txt" }, fixture.CommittedPaths);
        Assert.HasCount(2, fixture.Model.UnstagedChanges);
    }

    [TestMethod]
    public async Task Commit_StageFailure_DoesNotPrepareOrOpenDialogOrCommit()
    {
        Fixture fixture = new() { FailStage = true };

        await fixture.Model.OpenCommitDialogCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "stage" }, fixture.Calls);
        Assert.IsNull(fixture.Request);
        Assert.IsEmpty(fixture.CommittedPaths);
        Assert.IsFalse(fixture.Model.IsGitOperationRunning);
    }

    [TestMethod]
    public async Task Commit_CancelDialog_LeavesFilesStagedWithoutCommit()
    {
        Fixture fixture = new() { CancelDialog = true };

        await fixture.Model.OpenCommitDialogCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "stage", "prepare", "dialog" }, fixture.Calls);
        Assert.HasCount(2, fixture.Model.StagedChanges);
        Assert.IsEmpty(fixture.CommittedPaths);
    }

    [TestMethod]
    public async Task Amend_DoesNotAutomaticallyStageUnstagedFiles()
    {
        Fixture fixture = new(hasStagedChanges: true);

        await fixture.Model.OpenAmendDialogCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "dialog", "amend" }, fixture.Calls);
        Assert.HasCount(2, fixture.Model.UnstagedChanges);
    }

    [TestMethod]
    public async Task Commit_CleanRepository_PreservesEmptyCommitPreparation()
    {
        Fixture fixture = new();
        fixture.Model.UnstagedChanges.Clear();

        await fixture.Model.OpenCommitDialogCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "prepare", "dialog", "commit" }, fixture.Calls);
        Assert.IsEmpty(fixture.CommittedPaths);
    }

    private sealed class Fixture
    {
        public ChangesViewModel Model { get; }
        public List<string> Calls { get; } = [];
        public string[] CommittedPaths { get; private set; } = [];
        public CommitDialogRequest? Request { get; private set; }
        public bool FailStage { get; init; }
        public bool CancelDialog { get; init; }

        public Fixture(bool hasStagedChanges = false)
        {
            StrongReferenceMessenger messenger = new();
            ILocalizationService localization = Stub.Create<ILocalizationService>((method, args) => method switch
            {
                "GetString" => args![0],
                "get_CurrentLanguage" => AppLanguage.English,
                _ => throw new NotSupportedException(method)
            });
            IGitStagingService staging = Stub.Create<IGitStagingService>((method, _) =>
            {
                Assert.AreEqual("StageAllAsync", method);
                Calls.Add("stage");
                if (FailStage)
                {
                    throw new FileNotFoundException();
                }

                foreach (GitChangedFile file in Model!.UnstagedChanges.ToArray())
                {
                    Model.StagedChanges.Add(new(file.Path, file.Status, state: GitChangeState.Staged));
                }
                Model.UnstagedChanges.Clear();
                return Task.CompletedTask;
            });
            IGitCommitWorkflowService workflow = Stub.Create<IGitCommitWorkflowService>((method, _) =>
            {
                if (method == "PrepareCreateAsync")
                {
                    Calls.Add("prepare");
                    return Task.FromResult<bool?>(Model!.StagedChanges.Count == 0);
                }

                Calls.Add(method == "AmendAsync" ? "amend" : "commit");
                CommittedPaths = Model!.StagedChanges.Select(file => file.Path).ToArray();
                return Task.FromResult(GitCommitOperationResult.Succeeded("created"));
            });
            IGitHistoryService history = Stub.Create<IGitHistoryService>((_, _) => Task.FromResult(true));
            IGitStashService stashes = Stub.Create<IGitStashService>((_, _) =>
                Task.FromResult<IReadOnlyList<GitStash>>([]));
            IGitService git = Stub.Create<IGitService>((method, args) => method switch
            {
                "get_Staging" => staging,
                "get_CommitWorkflow" => workflow,
                "get_History" => history,
                "get_Stashes" => stashes,
                "ExecuteAsync" => ((Func<Task>)args![0]!)(),
                "GetStatusAsync" => Task.FromResult(new GitStatusSnapshot(
                    Model!.StagedChanges.ToArray(), Model.UnstagedChanges.ToArray(), [])),
                "GetOperationStateAsync" => Task.FromResult(GitOperationState.None),
                _ => throw new NotSupportedException(method)
            });
            IDialogService dialog = Stub.Create<IDialogService>((method, args) =>
            {
                Assert.AreEqual("ShowCommitDialogAsync", method);
                Calls.Add("dialog");
                Request = (CommitDialogRequest)args![0]!;
                return Task.FromResult(CancelDialog ? null : new CommitDialogResult(
                    "message", Request.Mode == CommitDialogMode.Amend));
            });
            ISettingsService settings = Stub.Create<ISettingsService>((_, _) => new AppSettings());
            IAsyncCommandExecutor executor = Stub.Create<IAsyncCommandExecutor>((_, args) => ((Func<Task>)args![0]!)());
            MainWindowViewModel window = new(
                Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
                localization, git, null!, new TestProductInfoService(), messenger, settings);
            window.SetCurrentRepository(new("C:\\repository", "repository", "main"), []);
            ConflictEditorViewModel conflictEditor = new(null!, git, localization, messenger, executor);
            Model = new(window, git, localization, null!, dialog, settings, null!, conflictEditor, messenger, executor);
            typeof(ChangesViewModel).GetField("_suppressSelectionLoad", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Model, true);
            if (hasStagedChanges)
            {
                Model.StagedChanges.Add(new("first.txt", "Modified", state: GitChangeState.Staged));
            }
            Model.UnstagedChanges.Add(new("first.txt", "Modified", state: GitChangeState.Unstaged));
            Model.UnstagedChanges.Add(new("second.txt", "Untracked", state: GitChangeState.Unstaged));
            typeof(ChangesViewModel).GetMethod("ReplaceAllChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Model, null);
        }
    }
}
