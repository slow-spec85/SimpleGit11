using Microsoft.Extensions.DependencyInjection;

namespace SimpleGit11.Extensibility.Plugins;

public interface ISimpleGitPlugin
{
    PluginMetadata Metadata { get; }

    void ConfigureServices(IServiceCollection services);
}
