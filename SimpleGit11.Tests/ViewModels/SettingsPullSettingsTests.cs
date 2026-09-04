using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class SettingsPullSettingsTests
{
    [TestMethod]
    [DataRow(null, null)]
    [DataRow("m", "yes")]
    [DataRow("", "unexpected")]
    public async Task Save_UnchangedValuesInBothScopesArePreserved(string? rebase, string? fastForward)
    {
        Fixture fixture = new();
        fixture.Runner.SetValues("--global", rebase, fastForward);
        fixture.Runner.SetValues("--local", rebase, fastForward);
        fixture.OpenRepository();

        await fixture.ViewModel.RefreshSettingsAsync();

        Assert.IsTrue(fixture.ViewModel.IsGlobalPullSettingsLoaded);
        Assert.IsTrue(fixture.ViewModel.IsRepositoryPullSettingsLoaded);
        Assert.AreEqual(rebase, fixture.ViewModel.SelectedGlobalPullRebase.Value);
        Assert.AreEqual(fastForward, fixture.ViewModel.SelectedGlobalPullFastForward.Value);
        Assert.AreEqual(rebase, fixture.ViewModel.SelectedRepositoryPullRebase.Value);
        Assert.AreEqual(fastForward, fixture.ViewModel.SelectedRepositoryPullFastForward.Value);
        await fixture.ViewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);
        Assert.IsEmpty(fixture.Runner.PullWrites);
        Assert.AreEqual("RepositorySettingsSaved", fixture.ViewModel.RepositorySettingsStatus);
    }

    [TestMethod]
    public async Task Refresh_ScopesKeepIndependentOptionsAndSelections()
    {
        Fixture fixture = new();
        fixture.Runner.SetValues("--global", "m", "yes");
        fixture.Runner.SetValues("--local", "interactive", "only");
        fixture.OpenRepository();

        await fixture.ViewModel.RefreshSettingsAsync();

        Assert.AreEqual("m", fixture.ViewModel.SelectedGlobalPullRebase.DisplayName);
        Assert.AreEqual("yes", fixture.ViewModel.SelectedGlobalPullFastForward.DisplayName);
        Assert.AreEqual("interactive", fixture.ViewModel.SelectedRepositoryPullRebase.Value);
        Assert.AreEqual("only", fixture.ViewModel.SelectedRepositoryPullFastForward.Value);
        Assert.IsFalse(fixture.ViewModel.RepositoryPullRebaseOptions.Any(option => option.Value == "m"));
        Assert.IsFalse(fixture.ViewModel.RepositoryPullFastForwardOptions.Any(option => option.Value == "yes"));

        fixture.Runner.SetValues("--global", "", null);
        await fixture.ViewModel.RefreshSettingsAsync();
        Assert.AreEqual("PullEmptyValue", fixture.ViewModel.SelectedGlobalPullRebase.DisplayName);
        Assert.IsFalse(fixture.ViewModel.GlobalPullRebaseOptions.Any(option => option.Value == "m"));
        Assert.AreEqual("only", fixture.ViewModel.SelectedRepositoryPullFastForward.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Save_OnlyChangesSelectedScopeAndSupportsUnset(bool local)
    {
        Fixture fixture = new();
        fixture.OpenRepository();
        await fixture.ViewModel.RefreshSettingsAsync();
        SettingsViewModel viewModel = fixture.ViewModel;
        if (local)
        {
            viewModel.SelectedRepositoryPullRebase = viewModel.RepositoryPullRebaseOptions.Single(option => option.Value == "false");
            viewModel.SelectedRepositoryPullFastForward = viewModel.RepositoryPullFastForwardOptions.Single(option => option.Value == "true");
        }
        else
        {
            viewModel.SelectedGlobalPullRebase = viewModel.GlobalPullRebaseOptions.Single(option => option.Value == "false");
            viewModel.SelectedGlobalPullFastForward = viewModel.GlobalPullFastForwardOptions.Single(option => option.Value == "true");
        }

        await viewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);

        string scope = local ? "--local" : "--global";
        CollectionAssert.AreEqual(new[]
        {
            $"config {scope} --replace-all pull.rebase false",
            $"config {scope} --replace-all pull.ff true"
        }, fixture.Runner.PullWrites);
        await viewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);
        Assert.HasCount(2, fixture.Runner.PullWrites);

        if (local)
        {
            viewModel.SelectedRepositoryPullRebase = viewModel.RepositoryPullRebaseOptions[0];
        }
        else
        {
            viewModel.SelectedGlobalPullRebase = viewModel.GlobalPullRebaseOptions[0];
        }

        await viewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);
        Assert.AreEqual($"config {scope} --unset-all pull.rebase", fixture.Runner.PullWrites[^1]);
        Assert.HasCount(3, fixture.Runner.PullWrites);
    }

    [TestMethod]
    public async Task Refresh_ClosingRepositoryDiscardsItsUnsavedPullSettings()
    {
        Fixture fixture = new();
        fixture.OpenRepository();
        await fixture.ViewModel.RefreshSettingsAsync();
        fixture.ViewModel.SelectedRepositoryPullRebase = fixture.ViewModel.RepositoryPullRebaseOptions[1];

        fixture.MainWindow.CloseCurrentRepository();
        await fixture.ViewModel.RefreshSettingsAsync();
        await fixture.ViewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);

        Assert.IsFalse(fixture.ViewModel.IsRepositoryPullSettingsLoaded);
        Assert.IsNull(fixture.ViewModel.SelectedRepositoryPullRebase.Value);
        Assert.IsEmpty(fixture.Runner.PullWrites);
        Assert.IsTrue(fixture.ViewModel.IsGlobalPullSettingsLoaded);
    }

    [TestMethod]
    [DataRow("--global")]
    [DataRow("--local")]
    public async Task Refresh_FailedReadClearsSavedSnapshotAndPreventsPullWrites(string scope)
    {
        Fixture fixture = new();
        fixture.OpenRepository();
        await fixture.ViewModel.RefreshSettingsAsync();
        fixture.Runner.FailReadScope = scope;

        await fixture.ViewModel.RefreshSettingsAsync();
        fixture.ViewModel.SelectedGlobalPullRebase = fixture.ViewModel.GlobalPullRebaseOptions[0];
        fixture.ViewModel.SelectedRepositoryPullRebase = fixture.ViewModel.RepositoryPullRebaseOptions[1];
        if (scope == "--global")
        {
            fixture.ViewModel.SelectedGlobalPullRebase = fixture.ViewModel.GlobalPullRebaseOptions[1];
            Assert.IsFalse(fixture.ViewModel.IsGlobalPullSettingsLoaded);
        }

        Assert.IsFalse(fixture.ViewModel.IsRepositoryPullSettingsLoaded);
        await fixture.ViewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);
        Assert.IsEmpty(fixture.Runner.PullWrites);
    }

    [TestMethod]
    public async Task Save_PartialFailureRetriesOnlyTheFailedValue()
    {
        Fixture fixture = new();
        await fixture.ViewModel.RefreshSettingsAsync();
        fixture.ViewModel.SelectedGlobalPullRebase = fixture.ViewModel.GlobalPullRebaseOptions[1];
        fixture.ViewModel.SelectedGlobalPullFastForward = fixture.ViewModel.GlobalPullFastForwardOptions[1];
        fixture.Runner.FailWriteKey = "pull.ff";

        await fixture.ViewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);
        Assert.AreEqual("", fixture.ViewModel.RepositorySettingsStatus);
        Assert.AreEqual("GitConfigSaveFailed", fixture.MainWindow.NotificationMessage);

        fixture.Runner.FailWriteKey = null;
        await fixture.ViewModel.SaveRepositorySettingsCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[]
        {
            "config --global --replace-all pull.rebase false",
            "config --global --replace-all pull.ff true",
            "config --global --replace-all pull.ff true"
        }, fixture.Runner.PullWrites);
        Assert.AreEqual("RepositorySettingsSaved", fixture.ViewModel.RepositorySettingsStatus);
    }

    private sealed class Fixture
    {
        public ConfigRunner Runner { get; } = new();
        public MainWindowViewModel MainWindow { get; }
        public SettingsViewModel ViewModel { get; }

        public Fixture()
        {
            ILocalizationService localization = new TestLocalization();
            GitConfigService configuration = new(Runner);
            IGitService git = ServiceStub.Create<IGitService>((method, _) => method == "get_Configuration"
                ? configuration : throw new NotSupportedException(method));
            StrongReferenceMessenger messenger = new();
            MainWindow = new MainWindowViewModel(
                ServiceStub.Create<IRecentRepositoriesService>((method, _) => method == "Load"
                    ? Array.Empty<RepositoryInfo>() : throw new NotSupportedException(method)),
                localization,
                git,
                ServiceStub.Create<IClipboardService>(),
                new TestProductInfoService(),
                messenger);
            ViewModel = new SettingsViewModel(
                MainWindow,
                ServiceStub.Create<IThemeService>((method, _) => method == "get_CurrentTheme"
                    ? AppThemeMode.System : throw new NotSupportedException(method)),
                localization,
                ServiceStub.Create<ISettingsService>((method, _) => method == "get_Current"
                    ? new AppSettings() : throw new NotSupportedException(method)),
                git,
                ServiceStub.Create<IDialogService>(),
                new TestExecutionContextService(new InMemoryRepositoryFileSystem()),
                messenger,
                ServiceStub.Create<IAsyncCommandExecutor>((method, arguments) => method == "ExecuteAsync"
                    ? ((Func<Task>)arguments![0]!)() : throw new NotSupportedException(method)));
        }

        public void OpenRepository() => MainWindow.SetCurrentRepository(
            new RepositoryInfo(Environment.CurrentDirectory, "test", "main"), []);
    }

    // Strict interface stubs keep unrelated application services out of these ViewModel tests.
    public class ServiceStub : DispatchProxy
    {
        private Func<string, object?[]?, object?> _invoke = (method, _) => throw new NotSupportedException(method);

        public static T Create<T>(Func<string, object?[]?, object?>? invoke = null) where T : class
        {
            T instance = Create<T, ServiceStub>();
            if (invoke is not null)
            {
                ((ServiceStub)(object)instance)._invoke = invoke;
            }

            return instance;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _invoke(targetMethod!.Name, args);
    }

    private sealed class ConfigRunner : IGitCommandRunner
    {
        private readonly Dictionary<(string Scope, string Key), string> _values = [];
        public List<string> PullWrites { get; } = [];
        public string? FailReadScope { get; set; }
        public string? FailWriteKey { get; set; }

        public void SetValues(string scope, string? rebase, string? fastForward)
        {
            SetValue(scope, "pull.rebase", rebase);
            SetValue(scope, "pull.ff", fastForward);
        }

        private void SetValue(string scope, string key, string? value)
        {
            if (value is null)
                _values.Remove((scope, key));
            else
                _values[(scope, key)] = value;
        }

        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments,
            GitCommandOptions? options = null, CancellationToken cancellationToken = default)
        {
            string? key = arguments.FirstOrDefault(argument => argument is "pull.rebase" or "pull.ff");
            if (key is null)
                return Task.FromResult(new GitCommandResult(0, "", ""));

            string scope = arguments[1];
            if (arguments.Contains("--get"))
            {
                if (scope == FailReadScope)
                    return Task.FromResult(new GitCommandResult(128, "", "read failed"));
                return Task.FromResult(_values.TryGetValue((scope, key), out string? value)
                    ? new GitCommandResult(0, value + '\0', "")
                    : new GitCommandResult(1, "", ""));
            }

            PullWrites.Add(string.Join(' ', arguments));
            if (key == FailWriteKey)
                return Task.FromResult(new GitCommandResult(4, "", "write failed"));
            SetValue(scope, key, arguments.Contains("--unset-all") ? null : arguments[^1]);
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }

    private sealed class TestLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;
        public void ApplyLanguage() { }
        public void SetLanguage(AppLanguage language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
