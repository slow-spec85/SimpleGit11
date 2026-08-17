using System.Linq;

namespace SimpleGit11.Models;

public sealed class GitCommitReference(string name, GitCommitReferenceKind kind)
{
    public string Name { get; } = name;
    public string DisplayName => Name.Split('/').LastOrDefault() ?? Name;

    public GitCommitReferenceKind Kind { get; } = kind;

    public bool IsLocalBranch => Kind == GitCommitReferenceKind.LocalBranch;

    public bool IsRemoteBranch => Kind == GitCommitReferenceKind.RemoteBranch;

    public bool IsTag => Kind == GitCommitReferenceKind.Tag;
}
