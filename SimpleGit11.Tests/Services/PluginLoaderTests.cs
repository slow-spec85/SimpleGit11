using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Services.Plugins;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class PluginLoaderTests
{
    [TestMethod]
    public void Load_MissingPluginsDirectory_ReturnsEmptyCatalog()
    {
        using TemporaryDirectory temporaryDirectory = new();
        FakePluginActivator activator = new();
        ServiceCollection services = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            services,
            temporaryDirectory.GetPath("missing"),
            new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.IsEmpty(catalog.Failures);
        Assert.AreEqual(0, activator.CallCount);
    }

    [TestMethod]
    public void Load_ValidPlugin_ConfiguresServicesAndAddsMetadata()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string pluginsDirectory = temporaryDirectory.CreateDirectory("Plugins");
        CreatePluginFiles(temporaryDirectory, "Plugins/simplegit11.test", CreateManifest());
        FakePluginActivator activator = new();
        ServiceCollection services = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            services,
            pluginsDirectory,
            new Version(1, 0));

        Assert.HasCount(1, catalog.Plugins);
        Assert.IsEmpty(catalog.Failures);
        Assert.AreEqual(TestPlugin.MetadataValue, catalog.Plugins[0]);
        Assert.IsTrue(services.Any(static descriptor =>
            descriptor.ServiceType == typeof(PluginMetadata)));
    }

    [TestMethod]
    [DataRow("../SimpleGit11.Plugin.Test.dll")]
    [DataRow("..\\SimpleGit11.Plugin.Test.dll")]
    [DataRow("C:\\SimpleGit11.Plugin.Test.dll")]
    [DataRow("plugin:stream.dll")]
    [DataRow("plugin.exe")]
    public void Load_InvalidEntryAssembly_RejectsPluginBeforeActivation(string entryAssembly)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string pluginsDirectory = temporaryDirectory.CreateDirectory("Plugins");
        PluginManifest manifest = CreateManifest() with
        {
            EntryAssembly = entryAssembly
        };
        CreatePluginFiles(temporaryDirectory, "Plugins/simplegit11.test", manifest);
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(),
            pluginsDirectory,
            new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
        StringAssert.Contains(catalog.Failures[0].Message, "entryAssembly");
        Assert.AreEqual(0, activator.CallCount);
    }

    [TestMethod]
    public void Load_PluginConfigurationFails_PreservesHostRegistrations()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string pluginsDirectory = temporaryDirectory.CreateDirectory("Plugins");
        CreatePluginFiles(temporaryDirectory, "Plugins/simplegit11.test", CreateManifest());
        FakePluginActivator activator = new(configureShouldFail: true);
        ServiceCollection services = new();
        services.AddSingleton(new object());
        ServiceDescriptor hostRegistration = services[0];

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            services,
            pluginsDirectory,
            new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
        Assert.HasCount(1, services);
        Assert.AreSame(hostRegistration, services[0]);
    }

    [TestMethod]
    public void Load_UnsupportedApiVersion_RejectsPlugin()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string pluginsDirectory = temporaryDirectory.CreateDirectory("Plugins");
        PluginManifest manifest = CreateManifest() with
        {
            ApiVersion = "2.0"
        };
        CreatePluginFiles(temporaryDirectory, "Plugins/simplegit11.test", manifest);
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(),
            pluginsDirectory,
            new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
        StringAssert.Contains(catalog.Failures[0].Message, "not supported");
        Assert.AreEqual(0, activator.CallCount);
    }

    [TestMethod]
    public void Load_DuplicatePluginId_LoadsOnlyFirstPlugin()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string pluginsDirectory = temporaryDirectory.CreateDirectory("Plugins");
        CreatePluginFiles(temporaryDirectory, "Plugins/a", CreateManifest());
        CreatePluginFiles(temporaryDirectory, "Plugins/b", CreateManifest());
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(),
            pluginsDirectory,
            new Version(1, 0));

        Assert.HasCount(1, catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
        Assert.AreEqual(1, activator.CallCount);
    }

    [TestMethod]
    [DataRow("2.0", false)]
    [DataRow("invalid", false)]
    [DataRow("1.0-invalid", false)]
    [DataRow("1.0.0.0", true)]
    public void Load_MinimumHostVersion_IsValidated(string minimumVersion, bool expectedLoaded)
    {
        using TemporaryDirectory directory = new();
        CreatePluginFiles(directory, "Plugins/test", CreateManifest() with
        {
            MinimumHostVersion = minimumVersion
        });
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(), directory.GetPath("Plugins"), new Version(1, 0));

        Assert.HasCount(expectedLoaded ? 1 : 0, catalog.Plugins);
        Assert.HasCount(expectedLoaded ? 0 : 1, catalog.Failures);
        Assert.AreEqual(expectedLoaded ? 1 : 0, activator.CallCount);
    }

    [TestMethod]
    public void Load_CorruptManifest_DoesNotPreventLoadingNextPlugin()
    {
        using TemporaryDirectory directory = new();
        directory.CreateFile("Plugins/a/plugin.json", "{invalid json");
        CreatePluginFiles(directory, "Plugins/b", CreateManifest());

        using PluginCatalog catalog = new PluginLoader(new FakePluginActivator()).Load(
            new ServiceCollection(), directory.GetPath("Plugins"), new Version(1, 0));

        Assert.HasCount(1, catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
    }

    [TestMethod]
    public void Load_MetadataMismatch_DoesNotRegisterServices()
    {
        using TemporaryDirectory directory = new();
        CreatePluginFiles(directory, "Plugins/test", CreateManifest() with { Name = "Different name" });
        ServiceCollection services = new();

        using PluginCatalog catalog = new PluginLoader(new FakePluginActivator()).Load(
            services, directory.GetPath("Plugins"), new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.IsEmpty(services);
        Assert.HasCount(1, catalog.Failures);
        StringAssert.Contains(catalog.Failures[0].Message, "does not match");
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    public void Load_MissingManifestFields_IsReported(string manifestJson)
    {
        using TemporaryDirectory directory = new();
        directory.CreateFile("Plugins/test/plugin.json", manifestJson);
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(), directory.GetPath("Plugins"), new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
        Assert.AreEqual(0, activator.CallCount);
    }

    [TestMethod]
    public void Load_MissingAssembly_IsReportedBeforeActivation()
    {
        using TemporaryDirectory directory = new();
        directory.CreateFile("Plugins/test/plugin.json", JsonSerializer.Serialize(CreateManifest()));
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(), directory.GetPath("Plugins"), new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
        Assert.AreEqual(0, activator.CallCount);
    }

    [TestMethod]
    public void Load_OversizedManifest_IsRejectedBeforeActivation()
    {
        using TemporaryDirectory directory = new();
        directory.CreateFile("Plugins/test/plugin.json", new string(' ', 64 * 1024 + 1));
        FakePluginActivator activator = new();

        using PluginCatalog catalog = new PluginLoader(activator).Load(
            new ServiceCollection(), directory.GetPath("Plugins"), new Version(1, 0));

        Assert.HasCount(1, catalog.Failures);
        StringAssert.Contains(catalog.Failures[0].Message, "size limit");
        Assert.AreEqual(0, activator.CallCount);
    }

    [TestMethod]
    public void Load_RealInvalidAssembly_DoesNotStopCatalogLoading()
    {
        using TemporaryDirectory directory = new();
        CreatePluginFiles(directory, "Plugins/test", CreateManifest());

        using PluginCatalog catalog = new PluginLoader(new PluginAssemblyActivator()).Load(
            new ServiceCollection(), directory.GetPath("Plugins"), new Version(1, 0));

        Assert.IsEmpty(catalog.Plugins);
        Assert.HasCount(1, catalog.Failures);
    }

    private static PluginManifest CreateManifest() => new()
    {
        Id = TestPlugin.MetadataValue.Id,
        Name = TestPlugin.MetadataValue.Name,
        Version = TestPlugin.MetadataValue.Version,
        ApiVersion = TestPlugin.MetadataValue.ApiVersion,
        MinimumHostVersion = "1.0",
        EntryAssembly = "SimpleGit11.Plugin.Test.dll",
        EntryType = "SimpleGit11.Plugin.Test.TestPlugin"
    };

    private static void CreatePluginFiles(
        TemporaryDirectory temporaryDirectory,
        string relativePluginDirectory,
        PluginManifest manifest)
    {
        temporaryDirectory.CreateFile(
            Path.Combine(relativePluginDirectory, PluginApi.ManifestFileName),
            JsonSerializer.Serialize(manifest));
        // Invalid manifest paths must never be used to create files, even in test setup.
        temporaryDirectory.CreateFile(Path.Combine(relativePluginDirectory, "SimpleGit11.Plugin.Test.dll"));
    }

    private sealed class FakePluginActivator(bool configureShouldFail = false) : IPluginActivator
    {
        public int CallCount { get; private set; }

        public PluginActivation Activate(string assemblyPath, string entryType)
        {
            CallCount++;
            return new PluginActivation(
                new TestPlugin(configureShouldFail),
                new PluginLoadContext(typeof(PluginLoader).Assembly.Location),
                TestPlugin.MetadataValue);
        }
    }

    private sealed class TestPlugin(bool configureShouldFail) : ISimpleGitPlugin
    {
        public static PluginMetadata MetadataValue { get; } = new(
            "simplegit11.test",
            "Test plugin",
            "1.0.0",
            PluginApi.CurrentVersion);

        public PluginMetadata Metadata => MetadataValue;

        public void ConfigureServices(IServiceCollection services)
        {
            // A broken plugin must not be able to discard registrations already owned by the host.
            services.Clear();
            services.AddSingleton(Metadata);
            if (configureShouldFail)
            {
                throw new InvalidOperationException("Test plugin configuration failed.");
            }
        }
    }
}
