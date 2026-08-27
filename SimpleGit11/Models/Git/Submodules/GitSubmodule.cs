using System;
using System.Collections.Generic;

namespace SimpleGit11.Models;

public sealed record GitSubmodule(
    string Name,
    string Path,
    string FullPath,
    string Url,
    string Branch,
    string HeadCommit,
    string IndexCommit,
    string CheckedOutCommit,
    bool IsInitialized,
    bool HasTrackedChanges,
    bool HasUntrackedFiles,
    bool HasConflict,
    string ErrorMessage,
    IReadOnlyList<GitSubmodule> Children)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? System.IO.Path.GetFileName(Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar))
        : Name;

    public bool IsCommitChanged => IsInitialized
        && !string.IsNullOrWhiteSpace(IndexCommit)
        && !string.Equals(IndexCommit, CheckedOutCommit, StringComparison.OrdinalIgnoreCase);

    public bool IsStaged => !string.IsNullOrWhiteSpace(HeadCommit)
        && !string.IsNullOrWhiteSpace(IndexCommit)
        && !string.Equals(HeadCommit, IndexCommit, StringComparison.OrdinalIgnoreCase);

    public bool IsDirty => HasTrackedChanges || HasUntrackedFiles;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ShortHeadCommit => Abbreviate(HeadCommit);

    public string ShortIndexCommit => Abbreviate(IndexCommit);

    public string ShortCheckedOutCommit => Abbreviate(CheckedOutCommit);

    private static string Abbreviate(string value)
    {
        return value.Length > 7 ? value[..7] : value;
    }
}
