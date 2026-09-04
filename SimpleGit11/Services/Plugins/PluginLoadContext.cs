using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using SimpleGit11.Extensibility.Plugins;

namespace SimpleGit11.Services.Plugins;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly string[] SharedAssemblyNames =
    [
        typeof(ISimpleGitPlugin).Assembly.GetName().Name!,
        typeof(IServiceCollection).Assembly.GetName().Name!,
        typeof(IAsyncRelayCommand).Assembly.GetName().Name!,
        // These framework types must keep their identity across the WinUI host and plugins.
        "Microsoft.WinUI",
        "WinRT.Runtime",
        "Microsoft.Windows.SDK.NET"
    ];

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginAssemblyPath)
        : base($"SimpleGit11.Plugin:{pluginAssemblyPath}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginAssemblyPath))!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (Array.Exists(
                SharedAssemblyNames,
                sharedName => string.Equals(sharedName, assemblyName.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }

        if (HasFrameworkAssemblyName(assemblyName.Name))
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
                // Optional packages can also use System.* names without being supplied by the host.
            }
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is null)
        {
            return null;
        }

        PluginPathPolicy.EnsureContainedPath(_pluginDirectory, assemblyPath);
        return LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath is null)
        {
            return nint.Zero;
        }

        PluginPathPolicy.EnsureContainedPath(_pluginDirectory, libraryPath);
        return LoadUnmanagedDllFromPath(libraryPath);
    }

    private static bool HasFrameworkAssemblyName(string? name) => name is not null
        && (name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal)
            || name is "System" or "mscorlib" or "netstandard" or "WindowsBase"
                or "Microsoft.CSharp" or "Microsoft.VisualBasic" or "Microsoft.VisualBasic.Core");
}
