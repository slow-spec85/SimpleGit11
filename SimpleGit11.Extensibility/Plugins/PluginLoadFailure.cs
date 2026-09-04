namespace SimpleGit11.Extensibility.Plugins;

public sealed record PluginLoadFailure(
    string PluginDirectory,
    string Message);
