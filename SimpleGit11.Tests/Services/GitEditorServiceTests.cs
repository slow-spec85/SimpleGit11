using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;
using System.Reflection;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Commits;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitEditorServiceTests
{
    [TestMethod]
    [DataRow("\r")]
    [DataRow("\u2028")]
    public async Task HistoryDraft_AppliesWhenOriginalSubjectContainsEmbeddedLineSeparators(string separator)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        string originalMessage = $"Old title{separator}{separator}Old body{separator}# old editor hint";
        await repository.RunGitAsync("commit", "--amend", "-m", originalMessage);
        string[] hashes = (await repository.RunGitAsync("rev-list", "HEAD")).Split('\n');
        GitCommit[] commits = hashes[..2].Select(hash => new GitCommit(hash, hash[..7], "Author", "a@example.invalid", null,
            "Old title", originalMessage)).ToArray();
        HistoryEditSession draft = new(commits);
        draft.SetMessage(hashes[0], "New title\n\nNew body");
        bool planReceived = false;
        GitEditorService service = CreateService((sequence, text) =>
        {
            Assert.IsTrue(sequence);
            StringAssert.Contains(text, separator);
            planReceived = true;
            return Task.FromResult<string?>(draft.BuildPlan(text));
        });
        GitCommandResult result = await service.RunAsync(repository.Repository,
            ["rebase", "-i", "--force-rebase", "--keep-empty", "--no-autosquash", hashes[2]],
            preparedMessages: draft.PreparedMessages);
        Assert.IsTrue(planReceived);
        Assert.IsTrue(result.IsSuccess, result.CombinedOutput);
        Assert.AreEqual("New title\n\nNew body", await repository.RunGitAsync("log", "-1", "--format=%B"));
        Assert.AreEqual("3", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task HistoryDraft_CombinesReordersDeletesAndSavesExactMessage(bool remote, bool root)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        repository.WriteFile("fourth.txt", "fourth");
        await repository.CommitAllAsync("fourth");
        string[] hashes = (await repository.RunGitAsync("rev-list", "HEAD")).Split('\n');
        GitCommit[] commits = hashes.Select((hash, index) => new GitCommit(hash, hash[..7], "Author", "a@example.invalid", null,
            new[] { "fourth", "third", "second", "first" }[index], new[] { "fourth", "third", "second", "first" }[index])).ToArray();
        HistoryEditSession draft = new(root ? commits : commits[..3]);
        draft.Merge(hashes[1], "Объединены изменения\n\nПервый абзац.\n\nВторой абзац.");
        draft.Move(hashes[2], -1);
        draft.SetDeleted(hashes[0], true);
        GitEditorService service = CreateService((sequence, text) =>
        {
            Assert.IsTrue(sequence, "Prepared messages should not open a second editor.");
            return Task.FromResult<string?>(draft.BuildPlan(text));
        }, remote: remote);
        GitCommandResult result = await service.RunAsync(repository.Repository,
            ["rebase", "-i", "--force-rebase", "--reapply-cherry-picks", "--empty=keep", "--keep-empty",
                "--no-autosquash", "--no-update-refs", "--no-rebase-merges", "--no-fork-point", root ? "--root" : hashes[3]], preparedMessages: draft.PreparedMessages);
        Assert.IsTrue(result.IsSuccess, result.CombinedOutput);
        Assert.AreEqual("2", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        Assert.AreEqual("Объединены изменения\n\nПервый абзац.\n\nВторой абзац.", await repository.RunGitAsync("log", "-1", "--format=%B"));
        Assert.IsTrue(repository.FileExists("second.txt"));
        Assert.IsTrue(repository.FileExists("third.txt"));
        Assert.IsFalse(repository.FileExists("fourth.txt"));
        Assert.IsFalse(repository.FileExists(".git/rebase-merge/simplegit-messages.json"));
    }

    [TestMethod]
    public async Task PreparedMessages_SurviveNewServiceAndContinueAfterConflict()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "base\n");
        await repository.CommitAllAsync("base");
        string basis = await repository.RunGitAsync("rev-parse", "HEAD");
        repository.WriteFile("file.txt", "changed\n");
        await repository.CommitAllAsync("edit");
        string edited = await repository.RunGitAsync("rev-parse", "HEAD");
        repository.WriteFile("file.txt", "changed again\n");
        await repository.CommitAllAsync("later");
        string later = await repository.RunGitAsync("rev-parse", "HEAD");
        GitEditorService service = CreateService((sequence, _) =>
        {
            Assert.IsTrue(sequence);
            return Task.FromResult<string?>($"drop {edited}\nreword {later}\n");
        });
        GitCommandResult stopped = await service.RunAsync(repository.Repository, ["rebase", "-i", basis],
            preparedMessages: new Dictionary<string, string> { [later] = "Новый заголовок\n\nНовое тело\n" });
        Assert.IsFalse(stopped.IsSuccess);
        Assert.IsTrue(repository.FileExists(".git/rebase-merge/simplegit-messages.json"));
        repository.WriteFile("file.txt", "resolved\n");
        await repository.RunGitAsync("add", "file.txt");
        GitEditorService resumed = CreateService((_, text) => Task.FromResult<string?>(text));
        GitCommandResult finished = await resumed.RunAsync(repository.Repository, ["rebase", "--continue"]);
        Assert.IsTrue(finished.IsSuccess, finished.CombinedOutput);
        Assert.AreEqual("Новый заголовок\n\nНовое тело", await repository.RunGitAsync("log", "-1", "--format=%B"));
        Assert.IsFalse(repository.FileExists(".git/rebase-merge/simplegit-messages.json"));
    }

    [TestMethod]
    public async Task PreparedMessages_AreRemovedWhenRebaseIsAborted()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        string head = await repository.RunGitAsync("rev-parse", "HEAD");
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>("break\n" + text));
        await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD^"], preparedMessages: new Dictionary<string, string> { [head] = "new" });
        Assert.IsTrue(repository.FileExists(".git/rebase-merge/simplegit-messages.json"));
        GitCommandResult aborted = await service.RunAsync(repository.Repository, ["rebase", "--abort"]);
        Assert.IsTrue(aborted.IsSuccess, aborted.CombinedOutput);
        Assert.AreEqual(head, await repository.RunGitAsync("rev-parse", "HEAD"));
        Assert.IsFalse(repository.FileExists(".git/rebase-merge/simplegit-messages.json"));
    }

    [TestMethod]
    [DataRow("strip", false)]
    [DataRow("verbatim", true)]
    public async Task MessageCleanup_IsStillControlledByGitAfterLineEndingNormalization(string cleanup, bool keepComment)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        await repository.RunGitAsync("config", "commit.cleanup", cleanup);
        await repository.RunGitAsync("config", "core.commentChar", "#");
        const string body = "Описание с символом\u2028внутри строки";
        GitEditorService service = CreateService((sequence, _) =>
        {
            Assert.IsFalse(sequence);
            return Task.FromResult<string?>("Заголовок\r\r" + body + "\r# Строка комментария\r");
        });

        GitCommandResult result = await service.RunAsync(repository.Repository, ["commit", "--amend"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual("Заголовок\n\n" + body + (keepComment ? "\n# Строка комментария" : ""),
            await repository.RunGitAsync("log", "-1", "--format=%B"));
    }

    [TestMethod]
    [DataRow("\r", false)]
    [DataRow("\r\n", false)]
    [DataRow("\n", false)]
    [DataRow("\r", true)]
    [DataRow("\r\n", true)]
    [DataRow("\n", true)]
    public async Task Reword_NormalizesEditorLineEndingsSoGitSeparatesBodyAndRemovesItsComments(string newline, bool remote)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        await repository.RunGitAsync("config", "commit.cleanup", "strip");
        await repository.RunGitAsync("config", "core.commentChar", "#");
        const string subject = "Исправлен заголовок";
        const string body = "Описание изменений\n\nВторой абзац описания";
        bool commentsRequested = false;
        GitEditorService service = CreateService((sequence, text) =>
        {
            if (sequence)
            {
                return Task.FromResult<string?>(text.Replace("pick ", "reword ", StringComparison.Ordinal));
            }

            int commentsStart = text.IndexOf("# Please enter", StringComparison.Ordinal);
            Assert.IsTrue(commentsStart >= 0, text);
            commentsRequested = true;
            string message = subject + "\n\n" + body + "\n\n" + text[commentsStart..];
            return Task.FromResult<string?>(message.ReplaceLineEndings(newline));
        }, remote: remote);

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD^"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.IsTrue(commentsRequested);
        Assert.AreEqual(subject, await repository.RunGitAsync("log", "-1", "--format=%s"));
        Assert.AreEqual(body, await repository.RunGitAsync("log", "-1", "--format=%b"));
        Assert.AreEqual(subject + "\n\n" + body, await repository.RunGitAsync("log", "-1", "--format=%B"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectedCommitParent_IncludesSelectedCommitAndItsNewerDescendants(bool synchronized)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        using TemporaryDirectory remote = new();
        if (synchronized)
        {
            await repository.RunGitAsync("init", "--bare", remote.Path);
            await repository.RunGitAsync("remote", "add", "origin", remote.Path);
            await repository.RunGitAsync("push", "--set-upstream", "origin", "main");
            Assert.AreEqual("0\t0", await repository.RunGitAsync("rev-list", "--left-right", "--count", "HEAD...@{upstream}"));
        }
        string selected = await repository.RunGitAsync("rev-parse", "HEAD^");
        string newest = await repository.RunGitAsync("rev-parse", "HEAD");
        string parent = await repository.RunGitAsync("rev-parse", selected + "^");
        string[] plan = [];
        GitEditorService service = CreateService((sequence, text) =>
        {
            Assert.IsTrue(sequence);
            plan = Instructions(text);
            return Task.FromResult<string?>(null);
        });

        await service.RunAsync(repository.Repository, ["rebase", "--interactive", parent]);

        Assert.AreEqual(2, plan.Length);
        StringAssert.StartsWith(plan[0], "pick " + selected[..7]);
        StringAssert.StartsWith(plan[1], "pick " + newest[..7]);
        Assert.AreEqual(newest, await repository.RunGitAsync("rev-parse", "HEAD"));
    }

    [TestMethod]
    public async Task Reword_UsesGitPlanAndRequestsUnicodeMessage()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        bool sequenceRequested = false;
        bool messageRequested = false;
        GitEditorService service = CreateService((sequence, text) =>
        {
            if (sequence)
            {
                sequenceRequested = true;
                return Task.FromResult<string?>(text.Replace("pick ", "reword ", StringComparison.Ordinal));
            }
            messageRequested = true;
            return Task.FromResult<string?>("Исправленное сообщение\n\nПодробное описание\n");
        });

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD^"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.IsTrue(sequenceRequested);
        Assert.IsTrue(messageRequested);
        StringAssert.StartsWith(await repository.RunGitAsync("log", "-1", "--format=%B"), "Исправленное сообщение\n\nПодробное описание");
        Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(repository.Repository.Path, ".git"), "simplegit-editor-*").Any());
    }

    [TestMethod]
    public async Task Fixup_CombinesChangesAndKeepsFirstMessage()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        GitEditorService service = CreateService((sequence, text) =>
        {
            if (!sequence) { return Task.FromResult<string?>(text); }
            string[] instructions = Instructions(text);
            instructions[1] = instructions[1].Replace("pick ", "fixup ", StringComparison.Ordinal);
            return Task.FromResult<string?>(string.Join("\n", instructions) + "\n");
        });

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD~2"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual("2", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        Assert.AreEqual("second", await repository.RunGitAsync("log", "-1", "--format=%s"));
        Assert.AreEqual("second", repository.ReadFile("second.txt"));
        Assert.AreEqual("third", repository.ReadFile("third.txt"));
    }

    [TestMethod]
    public async Task Squash_RequestsCombinedMessage()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        int messages = 0;
        GitEditorService service = CreateService((sequence, text) =>
        {
            if (!sequence) { messages++; return Task.FromResult<string?>("combined\n"); }
            string[] instructions = Instructions(text);
            instructions[1] = instructions[1].Replace("pick ", "squash ", StringComparison.Ordinal);
            return Task.FromResult<string?>(string.Join("\n", instructions) + "\n");
        });

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD~2"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual(1, messages);
        Assert.AreEqual("combined", await repository.RunGitAsync("log", "-1", "--format=%s"));
    }

    [TestMethod]
    [DataRow("\r")]
    [DataRow("\r\n")]
    [DataRow("\n")]
    public async Task ReorderAndDrop_UsesExactEditedPlan(string newline)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        GitEditorService service = CreateService((_, text) =>
        {
            string[] instructions = Instructions(text);
            return Task.FromResult<string?>((instructions[1] + "\n" + instructions[0].Replace("pick ", "drop ", StringComparison.Ordinal) + "\n")
                .ReplaceLineEndings(newline));
        });

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD~2"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.IsFalse(repository.FileExists("second.txt"));
        Assert.AreEqual("third", repository.ReadFile("third.txt"));
    }

    [TestMethod]
    public async Task CancelPlan_DoesNotRewriteHistoryOrLeaveRebaseState()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        string original = await repository.RunGitAsync("rev-parse", "HEAD");
        GitEditorService service = CreateService((_, _) => Task.FromResult<string?>(null));

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD~2"]);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(original, await repository.RunGitAsync("rev-parse", "HEAD"));
        Assert.IsFalse(Directory.Exists(Path.Combine(repository.Repository.Path, ".git", "rebase-merge")));
    }

    [TestMethod]
    public async Task RootOnDetachedHead_IsAllowed()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        await repository.RunGitAsync("switch", "--detach");
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>(text));

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "--root"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual("3", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        Assert.AreEqual("HEAD", await repository.RunGitAsync("rev-parse", "--abbrev-ref", "HEAD"));
    }

    [TestMethod]
    public async Task Break_EditTodoAndContinue_WorkAfterCreatingNewService()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>("break\n" + text));
        GitCommandResult start = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD~2"]);
        Assert.IsTrue(start.IsSuccess, start.StandardError);
        Assert.IsTrue(Directory.Exists(Path.Combine(repository.Repository.Path, ".git", "rebase-merge")));

        GitEditorService resumed = CreateService((_, text) => Task.FromResult<string?>(text));
        GitCommandResult edit = await resumed.RunAsync(repository.Repository, ["rebase", "--edit-todo"]);
        Assert.IsTrue(edit.IsSuccess, edit.StandardError);
        GitCommandResult result = await resumed.RunAsync(repository.Repository, ["rebase", "--continue"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.IsFalse(Directory.Exists(Path.Combine(repository.Repository.Path, ".git", "rebase-merge")));
        Assert.AreEqual("3", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
    }

    [TestMethod]
    public async Task EditThenAbort_RestoresOriginalBranchHead()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        string original = await repository.RunGitAsync("rev-parse", "HEAD");
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>(text.Replace("pick ", "edit ", StringComparison.Ordinal)));
        GitCommandResult start = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD~2"]);
        Assert.IsTrue(start.IsSuccess, start.StandardError);

        GitCommandResult abort = await service.RunAsync(repository.Repository, ["rebase", "--abort"]);

        Assert.IsTrue(abort.IsSuccess, abort.StandardError);
        Assert.AreEqual(original, await repository.RunGitAsync("rev-parse", "HEAD"));
        Assert.AreEqual("main", await repository.RunGitAsync("branch", "--show-current"));
    }

    [TestMethod]
    public async Task Exec_IsPassedToGitWithoutFiltering()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>(text + "\nexec git update-ref refs/simplegit-test/executed HEAD\n"));

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD^"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual(await repository.RunGitAsync("rev-parse", "HEAD"), await repository.RunGitAsync("rev-parse", "refs/simplegit-test/executed"));
    }

    [TestMethod]
    public async Task Merges_UsesGitLabelsResetAndMergeInstructions()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        await repository.RunGitAsync("switch", "-c", "topic", "HEAD~2");
        repository.WriteFile("topic.txt", "topic");
        await repository.CommitAllAsync("topic");
        await repository.RunGitAsync("switch", "main");
        await repository.RunGitAsync("merge", "--no-ff", "topic", "-m", "merge topic");
        bool mergePlan = false;
        GitEditorService service = CreateService((sequence, text) =>
        {
            if (sequence) { mergePlan = text.Contains("label ") && text.Contains("reset ") && text.Contains("merge "); }
            return Task.FromResult<string?>(text);
        });

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "--root", "--rebase-merges"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.IsTrue(mergePlan);
        Assert.AreEqual("1", await repository.RunGitAsync("rev-list", "--merges", "--count", "HEAD"));
    }

    [TestMethod]
    public async Task CancellationWhileWaitingForEditor_TerminatesGitAndCleansBridge()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string?> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GitEditorService service = CreateService((_, _) => { entered.SetResult(); return reply.Task; });
        using CancellationTokenSource cancellation = new();
        Task<GitCommandResult> execution = service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD^"], cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
        Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(repository.Repository.Path, ".git"), "simplegit-editor-*").Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RemoteEditorBridge_UsesShellAdapterAndSameEditingProtocol(bool windowsPaths)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        GitEditorService service = CreateService((sequence, text) => Task.FromResult<string?>(sequence
            ? text.Replace("pick ", "reword ", StringComparison.Ordinal)
            : "remote message\n"), remote: true, windowsPaths: windowsPaths);

        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "-i", "HEAD^"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual("remote message", await repository.RunGitAsync("log", "-1", "--format=%s"));
        Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(repository.Repository.Path, ".git"), "simplegit-editor-*").Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Conflict_CanBeResolvedAndContinuedOrSkipped(bool skip)
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        await repository.RunGitAsync("switch", "-c", "topic", "HEAD~2");
        repository.WriteFile("first.txt", "topic");
        await repository.CommitAllAsync("topic modification");
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("first.txt", "main");
        await repository.CommitAllAsync("main modification");
        await repository.RunGitAsync("switch", "topic");
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>(text));
        GitCommandResult start = await service.RunAsync(repository.Repository, ["rebase", "-i", "main"]);
        Assert.IsFalse(start.IsSuccess);
        Assert.IsTrue(Directory.Exists(Path.Combine(repository.Repository.Path, ".git", "rebase-merge")));

        if (!skip)
        {
            repository.WriteFile("first.txt", "resolved");
            await repository.RunGitAsync("add", "first.txt");
        }
        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", skip ? "--skip" : "--continue"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual(skip ? "main" : "resolved", repository.ReadFile("first.txt"));
        Assert.IsFalse(Directory.Exists(Path.Combine(repository.Repository.Path, ".git", "rebase-merge")));
    }

    [TestMethod]
    public async Task LinkedWorktree_UsesItsOwnGitDirectory()
    {
        await using TemporaryGitRepository repository = await CreateHistoryAsync();
        string path = Path.Combine(repository.Repository.Path, "linked worktree");
        await repository.RunGitAsync("worktree", "add", "--detach", path);
        SimpleGit11.Models.RepositoryInfo linked = new(path, "linked", "HEAD");
        GitEditorService service = CreateService((sequence, text) => Task.FromResult<string?>(sequence
            ? text.Replace("pick ", "reword ", StringComparison.Ordinal) : "linked message\n"));

        GitCommandResult result = await service.RunAsync(linked, ["rebase", "-i", "HEAD^"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual("linked message", await repository.RunGitAsync("-C", path, "log", "-1", "--format=%s"));
        Assert.AreEqual("third", await repository.RunGitAsync("log", "-1", "--format=%s"));
    }

    [TestMethod]
    public async Task RootCommit_CanBeSplitByUnstagingAndAmendingFirstPart()
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("first.txt", "first");
        repository.WriteFile("second.txt", "second");
        await repository.CommitAllAsync("root");
        GitEditorService service = CreateService((_, text) => Task.FromResult<string?>(text.Replace("pick ", "edit ", StringComparison.Ordinal)));
        GitCommandResult start = await service.RunAsync(repository.Repository, ["rebase", "-i", "--root"]);
        Assert.IsTrue(start.IsSuccess, start.StandardError);

        GitCommandResult unstage = await service.RunAsync(repository.Repository, ["rm", "--cached", "-r", "--ignore-unmatch", "--", "."]);
        Assert.IsTrue(unstage.IsSuccess, unstage.StandardError);
        await repository.RunGitAsync("add", "first.txt");
        await repository.RunGitAsync("commit", "--amend", "-m", "first part");
        await repository.RunGitAsync("add", "second.txt");
        await repository.RunGitAsync("commit", "-m", "second part");
        GitCommandResult result = await service.RunAsync(repository.Repository, ["rebase", "--continue"]);

        Assert.IsTrue(result.IsSuccess, result.StandardError);
        Assert.AreEqual("2", await repository.RunGitAsync("rev-list", "--count", "HEAD"));
        Assert.AreEqual("first", repository.ReadFile("first.txt"));
        Assert.AreEqual("second", repository.ReadFile("second.txt"));
    }

    private static GitEditorService CreateService(Func<bool, string, Task<string?>> edit, bool remote = false, bool windowsPaths = false)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SimpleGit11.slnx"))) { root = root.Parent; }
        Assert.IsNotNull(root);
        string configuration = typeof(GitEditorService).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string helper = Path.Combine(root.FullName, "SimpleGit11", "bin", "x64", configuration, "net8.0-windows10.0.19041.0", "win-x64", "SimpleGit11.GitEditor.exe");
        GitCommandRunner runner = new();
        if (!remote)
        {
            return new GitEditorService(runner, edit, helperPath: helper);
        }

        IExecutionRuntime runtime = Stub.Create<IExecutionRuntime>((method, _) => method switch
        {
            "get_Git" => runner,
            "get_Files" => new LocalRepositoryFileSystem(),
            "get_Paths" => new TestRepositoryPathService(windowsPaths ? RepositoryPathStyle.Windows : RepositoryPathStyle.Posix),
            "get_Capabilities" => ExecutionCapabilities.Git | ExecutionCapabilities.ReadFiles | ExecutionCapabilities.WriteFiles,
            _ => throw new NotSupportedException(method)
        });
        IExecutionContextService context = Stub.Create<IExecutionContextService>((method, _) => method == "get_Current"
            ? new SimpleGit11.Services.Execution.ExecutionContext(Guid.NewGuid(), 0, "ssh-test", null, runtime)
            : throw new NotSupportedException(method));
        return new GitEditorService(runner, edit, context, helperPath: "Windows helper must not be required on remote hosts");
    }

    private static string[] Instructions(string text) => text.Split('\n')
        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')).ToArray();

    private static async Task<TemporaryGitRepository> CreateHistoryAsync()
    {
        TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("first.txt", "first");
        await repository.CommitAllAsync("first");
        repository.WriteFile("second.txt", "second");
        await repository.CommitAllAsync("second");
        repository.WriteFile("third.txt", "third");
        await repository.CommitAllAsync("third");
        return repository;
    }
}
