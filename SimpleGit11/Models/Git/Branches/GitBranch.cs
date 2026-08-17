using System;

namespace SimpleGit11.Models;

public sealed class GitBranch(
    string name,
    bool isCurrent,
    bool isRemote,
    string shortCommitHash,
    string lastCommitMessage,
    DateTime? lastCommitDate,
    string configDescription = "")
{
    public string Name { get; } = name;

    public bool IsCurrent { get; } = isCurrent;

    public bool IsRemote { get; } = isRemote;

    public bool IsLocal => !IsRemote;

    public string ShortCommitHash { get; } = shortCommitHash;

    public string LastCommitMessage { get; } = lastCommitMessage;
    public DateTime? LastCommitDate { get; } = lastCommitDate;

    public string ConfigDescription { get; } = configDescription;

    public bool HasConfigDescription => !string.IsNullOrWhiteSpace(ConfigDescription);

    public bool CanAddConfigDescription => IsLocal && !HasConfigDescription;

    public bool CanEditConfigDescription => IsLocal && HasConfigDescription;

    public string CommitMetadata => $"{LastCommitDate?.ToString("g") + "   " ?? ""}{ShortCommitHash}";

    public string CurrentIndicator => IsCurrent ? "*" : IsRemote ? "R" : "";

    public string Description => $"{LastCommitDate?.ToString("g") + "   " ?? ""}" +
                                 $"{ShortCommitHash}" +
                                 $"   {LastCommitMessage.Trim()}";

    public GitBranch WithConfigDescription(string description)
    {
        return new GitBranch(
            Name,
            IsCurrent,
            IsRemote,
            ShortCommitHash,
            LastCommitMessage,
            LastCommitDate,
            description);
    }
}
