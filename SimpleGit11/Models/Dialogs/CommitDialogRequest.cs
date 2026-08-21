using System.Collections.Generic;

namespace SimpleGit11.Models;

public enum CommitDialogMode
{
    Create,
    Merge,
    Amend,
    EditMessage
}

public sealed class CommitDialogRequest
{
    private CommitDialogRequest(
        CommitDialogMode mode,
        IReadOnlyList<GitChangedFile> changedFiles,
        string initialMessage,
        bool noLocalCommits = false)
    {
        Mode = mode;
        ChangedFiles = changedFiles;
        InitialMessage = initialMessage;
        NoLocalCommits = noLocalCommits;
    }

    public CommitDialogMode Mode { get; }

    public IReadOnlyList<GitChangedFile> ChangedFiles { get; }

    public string InitialMessage { get; }

    public bool NoLocalCommits { get; }

    public static CommitDialogRequest CreateCommit(IReadOnlyList<GitChangedFile> changedFiles)
    {
        return new CommitDialogRequest(CommitDialogMode.Create, changedFiles, "");
    }

    public static CommitDialogRequest CreateAmend(IReadOnlyList<GitChangedFile> changedFiles, bool noLocalCommits)
    {
        return new CommitDialogRequest(CommitDialogMode.Amend, changedFiles, "", noLocalCommits);
    }

    public static CommitDialogRequest CreateMerge(
        IReadOnlyList<GitChangedFile> changedFiles,
        string initialMessage)
    {
        return new CommitDialogRequest(
            CommitDialogMode.Merge,
            changedFiles,
            initialMessage);
    }

    public static CommitDialogRequest CreateMessageEdit(
        string initialMessage,
        IReadOnlyList<GitChangedFile> changedFiles)
    {
        return new CommitDialogRequest(
            CommitDialogMode.EditMessage,
            changedFiles,
            initialMessage);
    }
}
