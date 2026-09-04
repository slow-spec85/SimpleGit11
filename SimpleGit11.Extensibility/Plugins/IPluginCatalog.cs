using System.Collections.Generic;

namespace SimpleGit11.Extensibility.Plugins;

public interface IPluginCatalog
{
    IReadOnlyList<PluginMetadata> Plugins { get; }

    IReadOnlyList<PluginLoadFailure> Failures { get; }
}
