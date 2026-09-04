namespace SimpleGit11.Services.Plugins;

internal interface IPluginActivator
{
    PluginActivation Activate(string assemblyPath, string entryType);
}
