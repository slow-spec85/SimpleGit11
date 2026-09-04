using System;
using System.Reflection;
using SimpleGit11.Extensibility.Plugins;

namespace SimpleGit11.Services.Plugins;

internal sealed class PluginAssemblyActivator : IPluginActivator
{
    public PluginActivation Activate(string assemblyPath, string entryType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryType);

        PluginLoadContext loadContext = new(assemblyPath);
        try
        {
            Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            Type pluginType = assembly.GetType(entryType, throwOnError: true, ignoreCase: false)
                ?? throw new InvalidOperationException($"Plugin type '{entryType}' could not be found.");

            if (!pluginType.IsClass || !pluginType.IsVisible || pluginType.IsAbstract || pluginType.ContainsGenericParameters
                || !typeof(ISimpleGitPlugin).IsAssignableFrom(pluginType))
            {
                throw new InvalidOperationException(
                    $"Plugin type '{entryType}' must be a public, non-abstract class implementing {nameof(ISimpleGitPlugin)}.");
            }

            if (pluginType.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Plugin type '{entryType}' must have a public parameterless constructor.");
            }

            ISimpleGitPlugin plugin = (ISimpleGitPlugin)Activator.CreateInstance(pluginType)!;
            return new PluginActivation(plugin, loadContext, plugin.Metadata);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }
}
