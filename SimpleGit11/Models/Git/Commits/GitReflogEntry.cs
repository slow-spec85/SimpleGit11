using System;

namespace SimpleGit11.Models;

public sealed record GitReflogEntry(
    string NewHash,
    string PreviousHash,
    DateTimeOffset? OccurredAt,
    string Subject,
    string ActorName,
    string ActorEmail)
{
    public string ShortNewHash => ShortenHash(NewHash);

    public string ShortPreviousHash => ShortenHash(PreviousHash);

    public bool IsPossibleCreation => Subject.StartsWith("branch: Created from", StringComparison.OrdinalIgnoreCase)
        || Subject.StartsWith("commit (initial)", StringComparison.OrdinalIgnoreCase);

    private static string ShortenHash(string hash)
    {
        return hash.Length > 8 ? hash[..8] : hash;
    }
}
