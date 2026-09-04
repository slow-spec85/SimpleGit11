using SimpleGit11.Extensibility.Plugins;

namespace SimpleGit11.Services.Plugins;

internal sealed record PluginActivation(
    ISimpleGitPlugin Plugin,
    PluginLoadContext LoadContext,
    PluginMetadata Metadata);
