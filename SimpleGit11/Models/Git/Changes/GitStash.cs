namespace SimpleGit11.Models;

public sealed class GitStash
{
    public GitStash(string reference, string shortHash, string age, string message)
    {
        Reference = reference;
        ShortHash = shortHash;
        Age = age;
        Message = message;
    }

    public string Reference { get; }

    public string ShortHash { get; }

    public string Age { get; }

    public string Message { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Age)
        ? $"{Reference} {Message}"
        : $"{Reference} {Age} - {Message}";
}
