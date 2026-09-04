using System.Threading.Tasks;
using System.Collections.Generic;
using SimpleGit11.Models;


namespace SimpleGit11.Services;

public enum ConfigScope
{
    None = 0,
    Local,
    Global
}

public interface IGitConfigService
{
    Task<string> GetUserNameAsync(ConfigScope level, RepositoryInfo? repository);
    Task<string> GetUserEmailAsync(ConfigScope level, RepositoryInfo? repository);
    Task<string> GetCredentialHelperAsync(ConfigScope level, RepositoryInfo? repository);
    Task<string> GetInitialBranchNameAsync(ConfigScope level, RepositoryInfo? repository);
    Task<string> GetPushDefaultRemoteAsync(ConfigScope level, RepositoryInfo? repository);
    Task<GitPullSettings> GetPullSettingsAsync(ConfigScope level, RepositoryInfo? repository);
    Task SetPullRebaseAsync(ConfigScope level, RepositoryInfo? repository, string? value);
    Task SetPullFastForwardAsync(ConfigScope level, RepositoryInfo? repository, string? value);
    Task<string> GetGlobalSshCommandAsync();
    Task<IReadOnlyList<GitUrlRewrite>> GetGlobalUrlRewritesAsync();
    Task<bool> IsGlobalCredentialHelperManagerConfiguredAsync();
    Task<IReadOnlyDictionary<string, string>> GetBranchDescriptionsAsync(RepositoryInfo repository);
    Task<IReadOnlyDictionary<string, string>> GetBranchPushRemotesAsync(RepositoryInfo repository);

    Task SetUserNameAsync(ConfigScope level, RepositoryInfo? repository, string userName);
    Task SetUserEmailAsync(ConfigScope level, RepositoryInfo? repository, string userEmail);
    Task SetCredentialHelperAsync(ConfigScope level, RepositoryInfo? repository);
    Task SetInitialBranchNameAsync(ConfigScope level, RepositoryInfo? repository, string branchName);
    Task SetPushDefaultRemoteAsync(ConfigScope level, RepositoryInfo? repository, string remoteName);
    Task SetGlobalSshCommandAsync(string sshCommand);
    Task SetBranchUpstreamAsync(RepositoryInfo repository, string branchName, string remoteName);
    Task UnsetBranchUpstreamAsync(RepositoryInfo repository, string branchName);
    Task SetBranchPushRemoteAsync(RepositoryInfo repository, string branchName, string? remoteName);
    Task SetBranchDescriptionAsync(RepositoryInfo repository, string branchName, string description);
    Task SetGlobalCredentialHelperManagerAsync();
    Task AddGlobalUrlRewriteAsync(GitUrlRewrite rewrite);
    Task UpdateGlobalUrlRewriteAsync(GitUrlRewrite oldRewrite, GitUrlRewrite newRewrite);
    Task RemoveGlobalUrlRewriteAsync(GitUrlRewrite rewrite);
    Task UnsetGlobalCredentialHelperAsync();

    Task UnsetUserNameAsync(ConfigScope level, RepositoryInfo? repository);
    Task UnsetUserEmailAsync(ConfigScope level, RepositoryInfo? repository);
    Task UnsetCredentialHelperAsync(ConfigScope level, RepositoryInfo? repository);
    Task UnsetInitialBranchNameAsync(ConfigScope level, RepositoryInfo? repository);
    Task UnsetPushDefaultRemoteAsync(ConfigScope level, RepositoryInfo? repository);
    Task UnsetGlobalSshCommandAsync();
}
