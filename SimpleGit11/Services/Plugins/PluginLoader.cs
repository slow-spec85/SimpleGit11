using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SimpleGit11.Extensibility.Plugins;

namespace SimpleGit11.Services.Plugins;

internal sealed partial class PluginLoader(IPluginActivator activator)
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPluginActivator _activator = activator
        ?? throw new ArgumentNullException(nameof(activator));

    public PluginCatalog Load(
        IServiceCollection services,
        string pluginsDirectory,
        Version hostVersion)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
        ArgumentNullException.ThrowIfNull(hostVersion);

        pluginsDirectory = Path.GetFullPath(pluginsDirectory);
        if (!Directory.Exists(pluginsDirectory))
        {
            return new PluginCatalog([], []);
        }

        List<PluginActivation> activations = [];
        List<PluginLoadFailure> failures = [];
        HashSet<string> pluginIds = new(StringComparer.OrdinalIgnoreCase);

        string[] pluginDirectories;
        try
        {
            PluginPathPolicy.EnsureNotReparsePoint(pluginsDirectory);
            pluginDirectories = Directory.GetDirectories(pluginsDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PluginCatalog([], [new PluginLoadFailure(pluginsDirectory, exception.Message)]);
        }

        foreach (string pluginDirectory in pluginDirectories
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            LoadPlugin(
                services,
                pluginDirectory,
                hostVersion,
                pluginIds,
                activations,
                failures);
        }

        return new PluginCatalog(activations, failures);
    }

    private void LoadPlugin(
        IServiceCollection services,
        string pluginDirectory,
        Version hostVersion,
        ISet<string> pluginIds,
        ICollection<PluginActivation> activations,
        ICollection<PluginLoadFailure> failures)
    {
        PluginActivation? activation = null;
        try
        {
            PluginManifest manifest = ReadAndValidateManifest(pluginDirectory, hostVersion);
            if (pluginIds.Contains(manifest.Id))
            {
                throw new InvalidOperationException($"Plugin id '{manifest.Id}' is registered more than once.");
            }

            string assemblyPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
            activation = _activator.Activate(assemblyPath, manifest.EntryType);
            ValidateMetadata(manifest, activation.Metadata);
            ServiceCollection pluginServices = new();
            activation.Plugin.ConfigureServices(pluginServices);
            foreach (ServiceDescriptor descriptor in pluginServices)
            {
                services.Add(descriptor);
            }

            activations.Add(activation);
            pluginIds.Add(manifest.Id);
        }
        catch (Exception exception)
        {
            activation?.LoadContext.Unload();
            failures.Add(new PluginLoadFailure(pluginDirectory, exception.GetBaseException().Message));
        }
    }

    private static PluginManifest ReadAndValidateManifest(
        string pluginDirectory,
        Version hostVersion)
    {
        string manifestPath = Path.Combine(pluginDirectory, PluginApi.ManifestFileName);
        PluginPathPolicy.EnsureContainedPath(pluginDirectory, manifestPath);
        using FileStream manifestStream = File.OpenRead(manifestPath);
        if (manifestStream.Length > 64 * 1024)
        {
            throw new InvalidDataException("Plugin manifest exceeds the 64 KiB size limit.");
        }
        PluginManifest manifest = JsonSerializer.Deserialize<PluginManifest>(
            manifestStream,
            ManifestSerializerOptions)
            ?? throw new InvalidDataException("Plugin manifest is empty.");

        ValidateRequiredValue(manifest.Id, nameof(manifest.Id));
        ValidateRequiredValue(manifest.Name, nameof(manifest.Name));
        ValidateRequiredValue(manifest.Version, nameof(manifest.Version));
        ValidateRequiredValue(manifest.ApiVersion, nameof(manifest.ApiVersion));
        ValidateRequiredValue(manifest.MinimumHostVersion, nameof(manifest.MinimumHostVersion));
        ValidateRequiredValue(manifest.EntryAssembly, nameof(manifest.EntryAssembly));
        ValidateRequiredValue(manifest.EntryType, nameof(manifest.EntryType));

        if (!PluginIdPattern().IsMatch(manifest.Id))
        {
            throw new InvalidDataException(
                $"Plugin id '{manifest.Id}' contains unsupported characters.");
        }

        if (!string.Equals(manifest.ApiVersion, PluginApi.CurrentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Plugin API version '{manifest.ApiVersion}' is not supported. Expected '{PluginApi.CurrentVersion}'.");
        }

        if (!Version.TryParse(manifest.MinimumHostVersion, out Version? minimumHostVersion))
        {
            throw new InvalidDataException(
                $"Minimum host version '{manifest.MinimumHostVersion}' is invalid.");
        }

        if (NormalizeVersion(hostVersion) < NormalizeVersion(minimumHostVersion))
        {
            throw new InvalidDataException(
                $"Plugin requires SimpleGit11 {minimumHostVersion} or later.");
        }

        if (!string.Equals(
                manifest.EntryAssembly,
                Path.GetFileName(manifest.EntryAssembly),
                StringComparison.Ordinal)
            || manifest.EntryAssembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetExtension(manifest.EntryAssembly), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Plugin entryAssembly must be a DLL file name without a path.");
        }

        string assemblyPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
        PluginPathPolicy.EnsureContainedPath(pluginDirectory, assemblyPath);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Plugin entry assembly was not found.", assemblyPath);
        }

        return manifest;
    }

    private static void ValidateMetadata(PluginManifest manifest, PluginMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (!string.Equals(manifest.Id, metadata.Id, StringComparison.Ordinal)
            || !string.Equals(manifest.Name, metadata.Name, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, metadata.Version, StringComparison.Ordinal)
            || !string.Equals(manifest.ApiVersion, metadata.ApiVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Plugin metadata returned by '{manifest.EntryType}' does not match its manifest.");
        }
    }

    private static void ValidateRequiredValue(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Plugin manifest property '{propertyName}' is required.");
        }
    }

    private static Version NormalizeVersion(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();
}
