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
        string initialMessage)
    {
        Mode = mode;
        ChangedFiles = changedFiles;
        InitialMessage = initialMessage;
    }

    public CommitDialogMode Mode { get; }

    public IReadOnlyList<GitChangedFile> ChangedFiles { get; }

    public string InitialMessage { get; }

    public static CommitDialogRequest CreateCommit(IReadOnlyList<GitChangedFile> changedFiles)
    {
        return new CommitDialogRequest(CommitDialogMode.Create, changedFiles, "");
    }

    public static CommitDialogRequest CreateAmend(IReadOnlyList<GitChangedFile> changedFiles)
    {
        return new CommitDialogRequest(CommitDialogMode.Amend, changedFiles, "");
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
