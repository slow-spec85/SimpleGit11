using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Extensibility.Plugins;

namespace SimpleGit11.Services.Plugins;

internal sealed class PluginCatalog : IPluginCatalog, IDisposable
{
    private readonly IReadOnlyList<PluginActivation> _activations;

    public PluginCatalog(
        IEnumerable<PluginActivation> activations,
        IEnumerable<PluginLoadFailure> failures)
    {
        _activations = activations.ToArray();
        Plugins = Array.AsReadOnly(_activations.Select(static activation => activation.Metadata).ToArray());
        Failures = Array.AsReadOnly(failures.ToArray());
    }

    public IReadOnlyList<PluginMetadata> Plugins { get; }

    public IReadOnlyList<PluginLoadFailure> Failures { get; }

    public void Dispose()
    {
        foreach (PluginActivation activation in _activations)
        {
            activation.LoadContext.Unload();
        }
    }
}
