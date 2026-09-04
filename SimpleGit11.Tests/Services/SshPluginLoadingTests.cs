using System.Reflection;
using System.Runtime.Loader;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Models;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.Services;
using SimpleGit11.Services.Execution;
using SimpleGit11.Services.Execution.Local;
using SimpleGit11.Services.Git.Execution;
using SimpleGit11.Services.Plugins;
using SimpleGit11.Tests.Presentation;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class SshPluginLoadingTests
{
    [TestMethod]
    public void Load_NoPlugin_OnlyLocalProviderAndNoMenuContribution()
    {
        using TemporaryDirectory directory = new();
        ServiceCollection services = CreateHostServices(
            new ConnectionTestContexts(),
            new DialogHost());
        using PluginCatalog catalog = new PluginLoader(new PluginAssemblyActivator()).Load(
            services, directory.GetPath("Plugins"), new Version(1, 0, 0));
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsEmpty(catalog.Plugins);
        Assert.IsEmpty(catalog.Failures);
        Assert.IsEmpty(provider.GetServices<IMainMenuContribution>());
        Assert.AreEqual("local", provider.GetServices<IExecutionProvider>().Single().Id);
    }

    [TestMethod]
    public async Task Load_InstalledPlugin_RegistersIsolatedProviderAndWorkingMenuCommand()
    {
        // Loaded native/managed dependencies can remain mapped until process exit on Windows.
        // Use the build-owned fixture rather than deleting loaded DLLs from a temporary directory.
        string installedPath = FixturePath;
        string pluginsPath = Path.GetDirectoryName(installedPath)!;
        ConnectionTestContexts contexts = new();
        contexts.Switch(false, "ssh");
        DialogHost dialogHost = new();
        ServiceCollection services = CreateHostServices(contexts, dialogHost);
        using PluginCatalog catalog = new PluginLoader(new PluginAssemblyActivator()).Load(
            services, pluginsPath, new Version(1, 0, 0));
        Assert.IsEmpty(catalog.Failures, string.Join("; ", catalog.Failures.Select(failure => failure.Message)));
        PluginMetadata metadata = catalog.Plugins.Single();
        Assert.AreEqual("simplegit11.ssh", metadata.Id);
        Assert.AreEqual(GetAssemblyVersion(typeof(App).Assembly), metadata.Version);
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        IExecutionProvider ssh = provider.GetServices<IExecutionProvider>().Single(item => item.Id == "ssh");
        AssemblyLoadContext pluginContext = AssemblyLoadContext.GetLoadContext(ssh.GetType().Assembly)!;
        Assert.AreNotSame(AssemblyLoadContext.Default, pluginContext);
        foreach (string dependency in new[] { "Renci.SshNet", "BouncyCastle.Cryptography", "Microsoft.Extensions.Logging.Abstractions" })
        {
            Assembly assembly = pluginContext.LoadFromAssemblyName(new AssemblyName(dependency));
            Assert.AreSame(pluginContext, AssemblyLoadContext.GetLoadContext(assembly));
            Assert.AreEqual(installedPath, Path.GetDirectoryName(assembly.Location));
        }

        IMainMenuContribution menu = provider.GetRequiredService<IMainMenuContribution>();
        Assert.AreEqual("Подключение по SSH", menu.Label);
        Assert.IsInstanceOfType<IAsyncRelayCommand>(menu.Command);
        Assert.AreEqual(MainMenuIndicatorKind.Success, menu.Indicator.Kind);
        using PluginMenuItem item = new(menu, action => action());
        await item.InvokeAsync();

        Assert.AreEqual(1, dialogHost.ConfirmationCount);
        Assert.IsTrue(contexts.Current.IsLocal);
        Assert.AreEqual(1, contexts.UseLocalCalls);
        Assert.AreEqual(MainMenuIndicatorKind.None, menu.Indicator.Kind);
        Assert.IsTrue(item.State.IsEnabled);
    }

    [TestMethod]
    public void ApplicationAndContracts_DoNotReferenceSshImplementation()
    {
        foreach (Assembly assembly in new[] { typeof(App).Assembly, typeof(IExecutionContextService).Assembly })
        {
            string[] forbiddenReferences = assembly.GetReferencedAssemblies()
                .Where(name => name.Name is "SimpleGit11.Plugin.Ssh" or "Renci.SshNet" or "BouncyCastle.Cryptography")
                .Select(name => name.Name!).ToArray();
            Assert.IsEmpty(forbiddenReferences);
            Assert.IsFalse(assembly.GetTypes().Any(type => type.Name.StartsWith("Ssh", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public void PluginLayout_ContainsOnlyPrivateRuntimeFiles()
    {
        string[] names = Directory.GetFiles(FixturePath).Select(Path.GetFileName).Order().ToArray()!;
        CollectionAssert.AreEquivalent(new[]
        {
            "plugin.json", "SimpleGit11.Plugin.Ssh.dll", "SimpleGit11.Plugin.Ssh.deps.json",
            "Renci.SshNet.dll", "BouncyCastle.Cryptography.dll", "Microsoft.Extensions.Logging.Abstractions.dll"
        }, names);
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "PluginFixtures", "Ssh");

    private static string GetAssemblyVersion(Assembly assembly)
    {
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        return informationalVersion.Split('+', 2)[0];
    }

    private static ServiceCollection CreateHostServices(
        ConnectionTestContexts contexts,
        DialogHost dialogHost)
    {
        ServiceCollection services = new();
        LocalExecutionRuntime local = new(new GitCommandRunner(), new LocalRepositoryFileSystem(),
            new LocalRepositoryPathService(), new LocalRepositoryFileTransfer());
        services.AddSingleton<IExecutionProvider>(new LocalExecutionProvider(local));
        services.AddSingleton<IExecutionContextService>(contexts);
        services.AddSingleton<ILocalSettingsStore>(new Settings());
        services.AddSingleton<ILocalizationService>(new Localization());
        services.AddSingleton<IPluginDialogHost>(dialogHost);
        return services;
    }

    private sealed class Settings : ILocalSettingsStore
    {
        public string? GetString(string key) => null;
        public void SetString(string key, string value) => throw new NotSupportedException();
    }

    private sealed class Localization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.Russian;
        public string GetString(string key) => key;
        public void ApplyLanguage() { }
        public void SetLanguage(AppLanguage language) { }
    }

    private sealed class DialogHost : IPluginDialogHost
    {
        public int ConfirmationCount { get; private set; }

        public Task<ContentDialogResult> ShowAsync(ContentDialog dialog) => throw new NotSupportedException();

        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
        {
            ConfirmationCount++;
            return Task.FromResult(true);
        }
    }
}
