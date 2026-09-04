using System.Runtime.Loader;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Presentation.Navigation;
using SimpleGit11.Services.Plugins;
using SimpleGit11.Tests.TestInfrastructure;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class PluginAssemblyActivatorTests
{
    [TestMethod]
    public async Task Activate_MenuContribution_ResolvesAndAwaitsCommandAcrossAssemblyContexts()
    {
        Type moduleType = typeof(PluginMenuTestModule);
        PluginActivation activation = new PluginAssemblyActivator().Activate(
            moduleType.Assembly.Location, moduleType.FullName!);
        try
        {
            ServiceCollection services = new();
            activation.Plugin.ConfigureServices(services);
            using ServiceProvider provider = services.BuildServiceProvider();
            IMainMenuContribution contribution = provider.GetRequiredService<IMainMenuContribution>();
            Assert.IsInstanceOfType<IAsyncRelayCommand>(contribution.Command);
            using PluginMenuItem item = new(contribution, action => action());

            Exception failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(item.InvokeAsync);

            Assert.AreEqual("Plugin command failed.", failure.Message);
            Assert.IsTrue(item.State.IsEnabled);
        }
        finally
        {
            activation.LoadContext.Unload();
        }
    }

    [TestMethod]
    public void Activate_RealAssembly_UsesSeparateContextAndSharedContracts()
    {
        Type moduleType = typeof(PluginLoaderTestModule);
        PluginActivation activation = new PluginAssemblyActivator().Activate(
            moduleType.Assembly.Location, moduleType.FullName!);
        try
        {
            Assert.AreSame(activation.LoadContext,
                AssemblyLoadContext.GetLoadContext(activation.Plugin.GetType().Assembly));
            Assert.AreNotSame(moduleType.Assembly, activation.Plugin.GetType().Assembly);
            Assert.AreSame(typeof(IAsyncRelayCommand).Assembly,
                activation.LoadContext.LoadFromAssemblyName(typeof(IAsyncRelayCommand).Assembly.GetName()));
            Assert.AreSame(typeof(ICommand).Assembly,
                activation.LoadContext.LoadFromAssemblyName(typeof(ICommand).Assembly.GetName()));
            System.Reflection.Assembly privateDependency = activation.LoadContext.LoadFromAssemblyName(
                typeof(ServiceProvider).Assembly.GetName());
            Assert.AreSame(activation.LoadContext, AssemblyLoadContext.GetLoadContext(privateDependency));
            ServiceCollection services = new();
            activation.Plugin.ConfigureServices(services);

            Assert.HasCount(1, services);
            Assert.AreEqual(typeof(PluginMetadata), services[0].ServiceType);
            Assert.AreEqual(activation.Metadata, services[0].ImplementationInstance);
        }
        finally
        {
            activation.LoadContext.Unload();
        }
    }

    [TestMethod]
    public void Activate_TypeWithoutPluginContract_IsRejected()
    {
        Type nonPluginType = typeof(PluginAssemblyActivatorTests);
        Assert.ThrowsExactly<InvalidOperationException>(() => new PluginAssemblyActivator().Activate(
            nonPluginType.Assembly.Location, nonPluginType.FullName!));
    }

    [TestMethod]
    public void Activate_MissingType_IsRejected()
    {
        Assert.ThrowsExactly<TypeLoadException>(() => new PluginAssemblyActivator().Activate(
            typeof(PluginLoaderTestModule).Assembly.Location, "Missing.Plugin.Type"));
    }

    [TestMethod]
    public void PluginPathPolicy_OutsideDependency_IsRejected()
    {
        using TemporaryDirectory directory = new();
        string pluginDirectory = directory.CreateDirectory("plugin");
        string outsidePath = directory.CreateFile("outside.dll");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PluginPathPolicy.EnsureContainedPath(pluginDirectory, outsidePath));
    }
}
