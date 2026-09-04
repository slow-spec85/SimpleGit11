using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SimpleGit11.Extensibility.Plugins;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Plugin.Ssh.Presentation;
using SimpleGit11.Plugin.Ssh.Services;
using SimpleGit11.Services.Execution;

namespace SimpleGit11.Plugin.Ssh;

public sealed class SshPlugin : ISimpleGitPlugin
{
    // Keep the original provider ID so saved context/profile identities remain compatible.
    public const string ProviderId = "ssh";
    public PluginMetadata Metadata => new(
        "simplegit11.ssh",
        "SSH",
        GetPluginVersion(),
        PluginApi.CurrentVersion);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IExecutionProvider, SshExecutionProvider>();
        services.AddSingleton<ISshConnectionProfileStore, SshConnectionProfileStore>();
        services.AddSingleton<ISshLocalizationService, SshLocalizationService>();
        services.AddSingleton<ISshConnectionDialogService, SshConnectionDialogService>();
        services.AddSingleton<SshConnectionController>();
        services.AddSingleton<IMainMenuContribution, SshMainMenuContribution>();
    }

    private static string GetPluginVersion()
    {
        Assembly assembly = typeof(SshPlugin).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        string? normalizedVersion = informationalVersion?.Split('+', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return normalizedVersion;
        }

        Version? version = assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
