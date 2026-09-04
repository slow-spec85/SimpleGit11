using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;

namespace SimpleGit11.Presentation.Commits;

internal readonly record struct CommitFilterCriteria(
    bool MainlineOnly,
    DateTimeOffset? FromDate,
    TimeSpan FromTime,
    DateTimeOffset? ToDate,
    TimeSpan ToTime,
    string SearchText,
    string ExactFilePathSearchText)
{
    public bool IsApplied => MainlineOnly
        || FromDate.HasValue
        || ToDate.HasValue
        || !string.IsNullOrWhiteSpace(SearchText);

    public bool IsExactFilePathSearch => !string.IsNullOrEmpty(ExactFilePathSearchText)
        && string.Equals(ExactFilePathSearchText, SearchText, StringComparison.Ordinal);
}

internal static class CommitBrowserFilter
{
    public static IReadOnlyList<GitCommit> Apply(
        IReadOnlyList<GitCommit> commits,
        CommitFilterCriteria criteria)
    {
        IEnumerable<GitCommit> filteredCommits = criteria.MainlineOnly
            ? GetMainlineCommits(commits)
            : commits;

        if (criteria.FromDate.HasValue)
        {
            DateTime from = criteria.FromDate.Value.Date + criteria.FromTime;
            filteredCommits = filteredCommits.Where(commit =>
                commit.AuthoredAt?.LocalDateTime >= from);
        }

        if (criteria.ToDate.HasValue)
        {
            DateTime toExclusive = criteria.ToDate.Value.Date
                + criteria.ToTime
                + TimeSpan.FromMinutes(1);
            filteredCommits = filteredCommits.Where(commit =>
                commit.AuthoredAt?.LocalDateTime < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            filteredCommits = filteredCommits.Where(commit => MatchesSearch(commit, criteria));
        }

        return filteredCommits.ToArray();
    }

    private static IEnumerable<GitCommit> GetMainlineCommits(IReadOnlyList<GitCommit> commits)
    {
        if (commits.Count == 0)
        {
            return [];
        }

        Dictionary<string, GitCommit> commitsByHash = commits.ToDictionary(
            commit => commit.Hash,
            StringComparer.OrdinalIgnoreCase);
        List<GitCommit> mainlineCommits = [];
        GitCommit? commit = commits[0];

        while (commit is not null)
        {
            mainlineCommits.Add(commit);
            commit = commit.ParentHashes.Count > 0
                && commitsByHash.TryGetValue(commit.ParentHashes[0], out GitCommit? parent)
                    ? parent
                    : null;
        }

        return mainlineCommits;
    }

    private static bool MatchesSearch(GitCommit commit, CommitFilterCriteria criteria)
    {
        string query = criteria.SearchText.Trim();
        if (criteria.IsExactFilePathSearch)
        {
            return commit.ChangedFilePaths.Any(path =>
                string.Equals(path, query, StringComparison.Ordinal));
        }

        return commit.Message.Contains(query, StringComparison.OrdinalIgnoreCase)
            || commit.Hash.Contains(query, StringComparison.OrdinalIgnoreCase)
            || commit.ShortHash.Contains(query, StringComparison.OrdinalIgnoreCase)
            || commit.AuthorName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || commit.AuthorEmail.Contains(query, StringComparison.OrdinalIgnoreCase)
            || commit.References.Any(reference =>
                reference.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            || commit.ChangedFilePaths.Any(path =>
                path.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
