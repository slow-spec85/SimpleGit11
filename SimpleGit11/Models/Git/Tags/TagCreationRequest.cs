namespace SimpleGit11.Models;

public sealed class TagCreationRequest(
    string tagName,
    string startPointHash,
    bool isAnnotated,
    string message)
{
    public string TagName { get; } = tagName;

    public string StartPointHash { get; } = startPointHash;

    public bool IsAnnotated { get; } = isAnnotated;

    public string Message { get; } = message;
}
