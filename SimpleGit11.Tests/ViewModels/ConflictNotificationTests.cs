using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Messages;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class ConflictNotificationTests
{
    [TestMethod]
    public async Task Conflict_ShowsWarningWithGuidanceRawDetailsAndExplicitNavigation()
    {
        Fixture fixture = new(() => Task.FromResult(ConflictedStatus()));
        GitCommandException exception = new("CONFLICT: raw Git output\nhint: resolve files", 1);
        int navigationCount = 0;
        fixture.Window.NavigationRequested += (_, args) =>
        {
            Assert.AreEqual(AppNavigationTarget.Changes, args.Target);
            navigationCount++;
        };

        Assert.IsTrue(await fixture.ShowAsync(exception));

        Assert.AreEqual(InfoBarSeverity.Warning, fixture.Window.NotificationSeverity);
        StringAssert.Contains(fixture.Window.NotificationMessage, "GitOperationConflictsTitle");
        StringAssert.Contains(fixture.Window.NotificationMessage, "ConflictResolutionRequiredOnChangesPage");
        Assert.AreEqual(exception.Message, fixture.Window.NotificationDetails);
        Assert.AreEqual("OpenChangesForConflictsButtonText", fixture.Window.NotificationActionText);
        Assert.AreEqual(0, navigationCount);
        Assert.IsNotNull(fixture.Window.NotificationActionCommand);
        fixture.Window.NotificationActionCommand.Execute(null);
        Assert.AreEqual(1, navigationCount);
        Assert.IsTrue(fixture.Window.TryConsumeChangesNotice(out string message, out string? details));
        Assert.AreEqual(fixture.Window.NotificationMessage, message);
        Assert.AreEqual(exception.Message, details);
    }

    [TestMethod]
    public async Task NoConflictedFiles_DoesNotClassifyErrorByItsTextOrOperationState()
    {
        Fixture fixture = new(() => Task.FromResult(new GitStatusSnapshot([], [], [])));

        Assert.IsFalse(await fixture.ShowAsync(new GitCommandException("CONFLICT in a remote message", 1)));
        Assert.IsFalse(fixture.Window.IsNotificationOpen);
    }

    [TestMethod]
    [DataRow(GitRemoteOperationErrorKind.Authentication)]
    [DataRow(GitRemoteOperationErrorKind.NonFastForward)]
    [DataRow(GitRemoteOperationErrorKind.AtomicNotSupported)]
    public async Task KnownRemoteFailure_IsNotHiddenByExistingLocalConflicts(GitRemoteOperationErrorKind kind)
    {
        Fixture fixture = new(() => throw new AssertFailedException("Status must not be queried."));

        Assert.IsFalse(await fixture.ShowAsync(new GitRemoteOperationException("remote failure", 1, kind)));
        Assert.IsFalse(fixture.Window.IsNotificationOpen);
    }

    [TestMethod]
    [DataRow("git")]
    [DataRow("directory")]
    [DataRow("access")]
    public async Task StatusCheckFails_PreservesOriginalErrorHandling(string failure)
    {
        Exception statusException = failure switch
        {
            "git" => new GitCommandException("status failed", 128),
            "directory" => new DirectoryNotFoundException(),
            _ => new UnauthorizedAccessException()
        };
        Fixture fixture = new(() => Task.FromException<GitStatusSnapshot>(statusException));

        Assert.IsFalse(await fixture.ShowAsync(new GitCommandException("original failure", 1)));
        Assert.IsFalse(fixture.Window.IsNotificationOpen);
    }

    [TestMethod]
    public async Task RepositoryChangesDuringStatusCheck_DoesNotShowWarningForOldRepository()
    {
        TaskCompletionSource<GitStatusSnapshot> status = new();
        Fixture fixture = new(() => status.Task);
        Task<bool> warning = fixture.ShowAsync(new GitCommandException("conflict", 1));
        fixture.Window.SetCurrentRepository(new RepositoryInfo("C:/other", "other", "main"), []);
        status.SetResult(ConflictedStatus());

        Assert.IsFalse(await warning);
        Assert.IsFalse(fixture.Window.IsNotificationOpen);
    }

    [TestMethod]
    public async Task OldWarning_CannotNavigateAfterRepositoryChanges()
    {
        Fixture fixture = new(() => Task.FromResult(ConflictedStatus()));
        Assert.IsTrue(await fixture.ShowAsync(new GitCommandException("conflict", 1)));
        System.Windows.Input.ICommand command = fixture.Window.NotificationActionCommand!;
        fixture.Window.CloseCurrentRepository();
        int navigationCount = 0;
        fixture.Window.NavigationRequested += (_, _) => navigationCount++;

        Assert.IsFalse(command.CanExecute(null));
        command.Execute(null);
        Assert.AreEqual(0, navigationCount);
    }

    [TestMethod]
    [DataRow("merge")]
    [DataRow("squash")]
    [DataRow("rebase")]
    [DataRow("revert")]
    [DataRow("cherry-pick")]
    [DataRow("cherry-pick-no-commit")]
    [DataRow("stash-apply")]
    [DataRow("stash-pop")]
    [DataRow("pull")]
    public async Task RealGitConflict_ProducesWarningIncludingOperationsWithoutSequencerState(string operation)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("file.txt", "base\n");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("switch", "-c", "feature");
        repository.WriteFile("file.txt", "feature\n");
        await repository.CommitAllAsync();
        string featureHash = await repository.RunGitAsync("rev-parse", "HEAD");
        await repository.RunGitAsync("switch", "main");
        if (operation.StartsWith("stash-", StringComparison.Ordinal))
        {
            repository.WriteFile("file.txt", "stashed\n");
            await repository.RunGitAsync("stash", "push");
        }
        repository.WriteFile("file.txt", "main\n");
        await repository.CommitAllAsync();

        IReadOnlyList<string> arguments = operation switch
        {
            "merge" => ["merge", "--no-edit", "feature"],
            "squash" => ["merge", "--squash", "feature"],
            "rebase" => ["rebase", "feature"],
            "revert" => ["revert", "--no-edit", featureHash],
            "cherry-pick" => ["cherry-pick", featureHash],
            "cherry-pick-no-commit" => ["cherry-pick", "--no-commit", featureHash],
            "stash-apply" => ["stash", "apply"],
            "stash-pop" => ["stash", "pop"],
            _ => ["pull", "--no-rebase", ".", "feature"]
        };
        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(
            () => new GitCommandRunner().RunAsync(repository.Repository.Path, arguments));
        GitStatusService statusService = new();
        Fixture fixture = new(() => statusService.GetStatusAsync(repository.Repository), repository.Repository);

        Assert.IsTrue(await fixture.ShowAsync(exception));
        Assert.AreEqual(InfoBarSeverity.Warning, fixture.Window.NotificationSeverity);
        Assert.AreEqual(exception.Message, fixture.Window.NotificationDetails);
        if (operation is "squash" or "stash-apply" or "stash-pop" or "cherry-pick-no-commit")
        {
            Assert.AreEqual(GitOperationKind.None,
                (await statusService.GetOperationStateAsync(repository.Repository)).Kind);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RebaseContinueOrSkip_NextCommitConflicts_ProducesWarning(bool skip)
    {
        await using TemporaryGitRepository repository = await TemporaryGitRepository.CreateAsync();
        repository.WriteFile("first.txt", "base\n");
        repository.WriteFile("second.txt", "base\n");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("switch", "-c", "feature");
        repository.WriteFile("first.txt", "feature\n");
        repository.WriteFile("second.txt", "feature\n");
        await repository.CommitAllAsync();
        await repository.RunGitAsync("switch", "main");
        repository.WriteFile("first.txt", "main\n");
        await repository.CommitAllAsync("first change");
        repository.WriteFile("second.txt", "main\n");
        await repository.CommitAllAsync("second change");
        await Assert.ThrowsAsync<GitCommandException>(() => new GitCommandRunner().RunAsync(
            repository.Repository.Path, ["rebase", "feature"]));
        if (!skip)
        {
            repository.WriteFile("first.txt", "resolved\n");
            await repository.RunGitAsync("add", "first.txt");
        }

        GitChangeRecoveryService recovery = new();
        GitCommandException exception = await Assert.ThrowsAsync<GitCommandException>(() => skip
            ? recovery.SkipOperationAsync(repository.Repository, GitOperationKind.Rebase)
            : recovery.ContinueOperationAsync(repository.Repository, GitOperationKind.Rebase));
        GitStatusService statusService = new();
        GitStatusSnapshot status = await statusService.GetStatusAsync(repository.Repository);
        Assert.AreEqual("second.txt", status.ConflictedChanges.Single().Path);
        Fixture fixture = new(() => Task.FromResult(status), repository.Repository);

        Assert.IsTrue(await fixture.ShowAsync(exception));
        Assert.AreEqual(InfoBarSeverity.Warning, fixture.Window.NotificationSeverity);
        Assert.AreEqual(exception.Message, fixture.Window.NotificationDetails);
    }

    private static GitStatusSnapshot ConflictedStatus() => new([], [],
        [new GitChangedFile("file.txt", "Conflict", state: GitChangeState.Conflicted)]);

    private sealed class Fixture
    {
        private readonly RepositoryInfo _repository;
        public MainWindowViewModel Window { get; }

        public Fixture(Func<Task<GitStatusSnapshot>> status, RepositoryInfo? repository = null)
        {
            _repository = repository ?? new RepositoryInfo("C:/repository", "repository", "main");
            IGitConfigService configuration = Stub.Create<IGitConfigService>((_, _) => Task.FromResult(""));
            IGitService git = Stub.Create<IGitService>((method, _) => method switch
            {
                "GetStatusAsync" => status(),
                "get_Configuration" => configuration,
                _ => throw new NotSupportedException(method)
            });
            Window = new MainWindowViewModel(
                Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
                new Localization(), git, Stub.Create<IClipboardService>(),
                new TestProductInfoService(), new StrongReferenceMessenger());
            Window.SetCurrentRepository(_repository, []);
        }

        public Task<bool> ShowAsync(GitCommandException exception) =>
            Window.TryShowConflictWarningAsync(_repository, this, exception);
    }

    public class Stub : DispatchProxy
    {
        private Func<string, object?[]?, object?> _invoke = (method, _) => throw new NotSupportedException(method);
        public static T Create<T>(Func<string, object?[]?, object?>? invoke = null) where T : class
        {
            T instance = Create<T, Stub>();
            if (invoke is not null) ((Stub)(object)instance)._invoke = invoke;
            return instance;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _invoke(targetMethod!.Name, args);
    }

    private sealed class Localization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;
        public string GetString(string resourceKey) => resourceKey;
        public void ApplyLanguage() { }
        public void SetLanguage(AppLanguage language) { }
    }
}
