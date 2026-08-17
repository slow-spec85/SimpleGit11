namespace SimpleGit11.ViewModels;

public sealed class BranchRemoteOption(string name, string? remoteName)
{
    public string Name { get; } = name;

    public string? RemoteName { get; } = remoteName;
}
