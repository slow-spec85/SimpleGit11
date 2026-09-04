using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Extensibility.Presentation;

namespace SimpleGit11.Tests.TestInfrastructure;

// Loaded into a separate AssemblyLoadContext by PluginAssemblyActivatorTests.
public sealed class PluginLoaderTestModule : ISimpleGitPlugin
{
    public PluginMetadata Metadata { get; } = new(
        "simplegit11.test", "Test plugin", "1.0.0", PluginApi.CurrentVersion);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Metadata);
    }
}

public sealed class PluginMenuTestModule : ISimpleGitPlugin
{
    public PluginMetadata Metadata { get; } = new(
        "simplegit11.test.menu", "Menu test plugin", "1.0.0", PluginApi.CurrentVersion);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMainMenuContribution, PluginTestMenuContribution>();
    }
}

public sealed class PluginTestMenuContribution : ObservableObject, IMainMenuContribution
{
    public string Id => "test.menu";
    public string Label => "Test menu";
    public string IconGlyph => string.Empty;
    public MainMenuPlacement Placement => MainMenuPlacement.Footer;
    public MainMenuIndicator Indicator => MainMenuIndicator.None;
    public ICommand Command { get; } = new AsyncRelayCommand(
        () => Task.FromException(new InvalidOperationException("Plugin command failed.")));
}
