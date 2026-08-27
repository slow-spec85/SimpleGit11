using SimpleGit11.Models;
using SimpleGit11.Services;
using SimpleGit11.Tests.TestInfrastructure;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class AboutDialogViewModelTests
{
    [TestMethod]
    public async Task LoadAsync_ReleaseAvailable_ExposesReleaseInformation()
    {
        TestProductInfoService productInfoService = new()
        {
            LatestRelease = new ProductReleaseInfo(
                "1.2.3",
                new Uri("https://github.com/slow-spec85/SimpleGit11/releases/tag/v1.2.3"),
                false)
        };
        TestSettingsService settingsService = new();
        using AboutDialogViewModel viewModel = new(
            productInfoService,
            settingsService,
            new TestLocalizationService());

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasLatestRelease);
        Assert.AreEqual("1.2.3", viewModel.LatestReleaseVersion);
        Assert.IsNotNull(viewModel.LatestReleaseUri);
        Assert.IsFalse(viewModel.HasReleaseStatus);
        CollectionAssert.AreEqual(
            new[] { false },
            productInfoService.IncludePrereleaseRequests);
    }

    [TestMethod]
    public async Task IncludePrereleaseVersions_Changed_PersistsAndReloadsRelease()
    {
        TestProductInfoService productInfoService = new();
        TestSettingsService settingsService = new();
        using AboutDialogViewModel viewModel = new(
            productInfoService,
            settingsService,
            new TestLocalizationService());
        await viewModel.LoadAsync();

        viewModel.IncludePrereleaseVersions = true;

        Assert.IsTrue(settingsService.Current.IncludePrereleaseVersions);
        CollectionAssert.AreEqual(
            new[] { false, true },
            productInfoService.IncludePrereleaseRequests);
    }

    [TestMethod]
    public async Task LoadAsync_RequestFails_ShowsLocalizedErrorState()
    {
        TestProductInfoService productInfoService = new()
        {
            ReleaseException = new HttpRequestException("Unavailable")
        };
        using AboutDialogViewModel viewModel = new(
            productInfoService,
            new TestSettingsService(),
            new TestLocalizationService());

        await viewModel.LoadAsync();

        Assert.IsTrue(viewModel.HasReleaseError);
        Assert.IsTrue(viewModel.HasReleaseStatus);
        Assert.AreEqual("AboutReleaseCheckFailed", viewModel.ReleaseStatusMessage);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public event EventHandler? EditorAppearanceChanged;

        public AppSettings Current { get; } = new();

        public void SetThemeMode(AppThemeMode themeMode) => Current.ThemeMode = themeMode;

        public void SetLanguage(AppLanguage language) => Current.Language = language;

        public void SetIgnoreWhitespaceInDiff(bool ignoreWhitespace) =>
            Current.IgnoreWhitespaceInDiff = ignoreWhitespace;

        public void SetIncludePrereleaseVersions(bool includePrereleaseVersions) =>
            Current.IncludePrereleaseVersions = includePrereleaseVersions;

        public void SetEditorFont(string fontFamily, int fontSize)
        {
            Current.EditorFontFamily = fontFamily;
            Current.EditorFontSize = fontSize;
            EditorAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetEditorLineSpacing(int lineSpacing)
        {
            Current.EditorLineSpacing = lineSpacing;
            EditorAppearanceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSshCommand(string sshCommand) => Current.SshCommand = sshCommand;
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;

        public string GetString(string resourceKey) => resourceKey;

        public void ApplyLanguage()
        {
        }

        public void SetLanguage(AppLanguage language)
        {
        }
    }
}
