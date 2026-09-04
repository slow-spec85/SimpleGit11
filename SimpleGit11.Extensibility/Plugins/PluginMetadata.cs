namespace SimpleGit11.Extensibility.Plugins;

public sealed record PluginMetadata(
    string Id,
    string Name,
    string Version,
    string ApiVersion);
