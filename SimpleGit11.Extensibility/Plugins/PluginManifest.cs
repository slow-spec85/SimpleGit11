namespace SimpleGit11.Extensibility.Plugins;

public sealed record PluginManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string MinimumHostVersion { get; init; } = string.Empty;

    public string EntryAssembly { get; init; } = string.Empty;

    public string EntryType { get; init; } = string.Empty;
}
