using CommunityToolkit.Mvvm.Messaging;
using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;
using Stub = SimpleGit11.Tests.ViewModels.SettingsPullSettingsTests.ServiceStub;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class MainWindowUserIdentityTests
{
    [TestMethod]
    [DataRow("Local User", "local@example.com", "global@example.com", "local@example.com")]
    [DataRow("Local User", "", "global@example.com", "global@example.com")]
    [DataRow("", "local@example.com", "global@example.com", "local@example.com")]
    [DataRow("Local User", "", "", null)]
    public async Task RefreshCurrentUser_ResolvesEmailIndependentlyOfName(
        string localName, string localEmail, string globalEmail, string? expectedEmail)
    {
        IGitConfigService configuration = Stub.Create<IGitConfigService>((method, arguments) =>
            Task.FromResult(method switch
            {
                "GetUserNameAsync" => (ConfigScope)arguments![0]! == ConfigScope.Local ? localName : "Global User",
                "GetUserEmailAsync" => (ConfigScope)arguments![0]! == ConfigScope.Local ? localEmail : globalEmail,
                _ => throw new NotSupportedException(method)
            }));
        MainWindowViewModel window = CreateWindow(configuration);
        window.SetCurrentRepository(new RepositoryInfo("C:/repo", "repo", "main"), []);

        await window.RefreshCurrentUserAsync();

        Assert.AreEqual(string.IsNullOrEmpty(localName) ? "Global User" : localName, window.CurrentUserName);
        Assert.AreEqual(expectedEmail, window.CurrentUserEmail);

        window.CloseCurrentRepository();
        await window.RefreshCurrentUserAsync();

        Assert.AreEqual("Global User", window.CurrentUserName);
        Assert.AreEqual(string.IsNullOrEmpty(globalEmail) ? null : globalEmail, window.CurrentUserEmail);
    }

    [TestMethod]
    public async Task RefreshCurrentUser_DiscardsEmailFromAnOlderRefresh()
    {
        TaskCompletionSource<string> pendingEmail = new();
        bool delayEmail = false;
        string currentEmail = "old@example.com";
        IGitConfigService configuration = Stub.Create<IGitConfigService>((method, _) => method switch
        {
            "GetUserNameAsync" => Task.FromResult("User"),
            "GetUserEmailAsync" => delayEmail ? pendingEmail.Task : Task.FromResult(currentEmail),
            _ => throw new NotSupportedException(method)
        });
        MainWindowViewModel window = CreateWindow(configuration);
        delayEmail = true;
        Task oldRefresh = window.RefreshCurrentUserAsync();
        Assert.IsNull(window.CurrentUserEmail);
        delayEmail = false;
        currentEmail = "new@example.com";

        await window.RefreshCurrentUserAsync();
        pendingEmail.SetResult("old@example.com");
        await oldRefresh;

        Assert.AreEqual("new@example.com", window.CurrentUserEmail);
    }

    private static MainWindowViewModel CreateWindow(IGitConfigService configuration)
    {
        return new MainWindowViewModel(
            Stub.Create<IRecentRepositoriesService>((_, _) => Array.Empty<RepositoryInfo>()),
            Stub.Create<ILocalizationService>((_, arguments) => arguments![0]),
            Stub.Create<IGitService>((method, _) => method == "get_Configuration"
                ? configuration : throw new NotSupportedException(method)),
            Stub.Create<IClipboardService>(),
            new TestProductInfoService(),
            new StrongReferenceMessenger(),
            Stub.Create<ISettingsService>((_, _) => new AppSettings()));
    }
}
