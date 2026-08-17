using System.IO;

namespace SimpleGit11.Models;

public sealed record GitWorktree(
    string Path,
    string HeadHash,
    string BranchName,
    bool IsBare,
    bool IsDetached,
    bool IsLocked,
    bool IsPrunable,
    bool IsMain = false,
    bool IsCurrent = false,
    string LockReason = "",
    string PrunableReason = "")
{
    public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(
        System.IO.Path.DirectorySeparatorChar,
        System.IO.Path.AltDirectorySeparatorChar));

    public string ShortHeadHash => HeadHash.Length > 7 ? HeadHash[..7] : HeadHash;
}
