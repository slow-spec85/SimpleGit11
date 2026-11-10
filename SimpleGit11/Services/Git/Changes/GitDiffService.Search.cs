using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed partial class GitDiffService
{
    public async Task<IReadOnlyList<GitFileSearchResult>> SearchFilesAsync(
        RepositoryInfo repository, GitCommit? commit, IReadOnlyList<GitChangedFile> files,
        string query, bool changedLinesOnly, CancellationToken cancellationToken, GitChangedFile? preferredFile = null)
    {
        if (!CanSearch(query)) { return []; }

        Dictionary<GitChangedFile, GitFileSearchResult> results = [];
        IEnumerable<GitChangedFile> searchOrder = preferredFile is null
            ? files : files.OrderByDescending(file => ReferenceEquals(file, preferredFile));
        foreach (GitChangedFile file in searchOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.IsSubmodule) { continue; }

            IReadOnlyList<GitFileSearchMatch> matches = [];
            if (!changedLinesOnly || file.IsUntracked)
            {
                matches = await SearchFileContentsAsync(repository, commit, file, query, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!file.IsUntracked && (changedLinesOnly || NeedsRemovedMatches(file)))
            {
                DiffResult diff = commit is null
                    ? await GetDiffAsync(repository, file, compareConflictToHead: true, cancellationToken).ConfigureAwait(false)
                    : await GetCommitDiffAsync(repository, commit, file, cancellationToken).ConfigureAwait(false);
                matches = AddPatchMatches(diff, file, query, changedLinesOnly, matches, cancellationToken);
            }

            if (matches.Count > 0)
            {
                results.Add(file, new GitFileSearchResult(commit, file, matches));
            }
        }

        return files.Where(results.ContainsKey).Select(file => results[file]).ToArray();
    }

    private async Task<IReadOnlyList<GitFileSearchMatch>> SearchFileContentsAsync(
        RepositoryInfo repository, GitCommit? commit, GitChangedFile file, string query,
        CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = commit is null
                ? await GetFullFileTextAsync(repository, file, cancellationToken).ConfigureAwait(false)
                : await GetCommitFileTextAsync(repository, commit, file, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            // The text-file service rejects binary files.
            return [];
        }

        if (content.Contains('\0')) { return []; }
        List<GitFileSearchMatch> matches = [];
        using StringReader reader = new(content);
        int lineNumber = 0;
        while (reader.ReadLine() is string line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddMatches(matches, query, line, ++lineNumber);
        }

        return matches;
    }

    public async Task<IReadOnlyList<GitFileSearchResult>> SearchHistoryAsync(
        RepositoryInfo repository, string query, bool changedLinesOnly, CancellationToken cancellationToken,
        string revision = "HEAD")
    {
        if (!CanSearch(query)) { return []; }

        GitCommandResult tip = await _commandRunner.RunAsync(
            repository.Path, ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            new GitCommandOptions(ThrowOnError: false), cancellationToken).ConfigureAwait(false);
        if (tip.ExitCode == 1) { return []; }
        if (!tip.IsSuccess) { throw new GitCommandException(tip.StandardError, tip.ExitCode); }

        string commitHash = tip.StandardOutput.Trim();
        List<(GitCommit Commit, GitChangedFile File, string Blob)> files = await ReadHistoryFilesAsync(
            repository, commitHash, changedLinesOnly ? query : null, cancellationToken).ConfigureAwait(false);
        if (changedLinesOnly)
        {
            List<GitFileSearchResult> changedResults = [];
            foreach (IGrouping<GitCommit, (GitCommit Commit, GitChangedFile File, string Blob)> group in files.GroupBy(file => file.Commit))
            {
                changedResults.AddRange(await SearchFilesAsync(
                    repository, group.Key, group.Select(file => file.File).ToArray(),
                    query, true, cancellationToken).ConfigureAwait(false));
            }

            return changedResults;
        }

        Dictionary<string, List<GitFileSearchMatch>> matchesByBlob = new(StringComparer.Ordinal);
        // Limit argument length and output per invocation. Renames and unchanged blobs
        // shared by multiple commits can all reuse the same search results.
        foreach (string[] blobs in files.Select(file => file.Blob).Distinct(StringComparer.Ordinal).Chunk(128))
        {
            GitCommandResult grep = await _commandRunner.RunAsync(
                repository.Path,
                ["-c", "grep.column=false", "grep", "-F", "-I", "-n", "-i", "-z", "--no-color",
                    "-e", query, .. blobs, "--"],
                new GitCommandOptions(ThrowOnError: false), cancellationToken).ConfigureAwait(false);
            if (grep.ExitCode == 1) { continue; }
            if (!grep.IsSuccess) { throw new GitCommandException(grep.StandardError, grep.ExitCode); }

            using StringReader reader = new(grep.StandardOutput);
            while (reader.ReadLine() is string line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int firstSeparator = line.IndexOf('\0');
                int secondSeparator = firstSeparator < 0 ? -1 : line.IndexOf('\0', firstSeparator + 1);
                if (secondSeparator < 0 || !int.TryParse(
                    line.AsSpan(firstSeparator + 1, secondSeparator - firstSeparator - 1),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int lineNumber))
                {
                    continue;
                }

                string blob = line[..firstSeparator];
                if (!matchesByBlob.TryGetValue(blob, out List<GitFileSearchMatch>? matches))
                {
                    matches = [];
                    matchesByBlob.Add(blob, matches);
                }

                AddMatches(matches, query, line[(secondSeparator + 1)..], lineNumber);
            }
        }

        // A blob search sees the file contents at the commit. Pickaxe also finds commits
        // where a matching line was removed, even when the new blob has no match.
        List<(GitCommit Commit, GitChangedFile File, string Blob)> patchCandidates = await ReadHistoryFilesAsync(
            repository, commitHash, query, cancellationToken).ConfigureAwait(false);
        HashSet<(string Commit, string Path)> candidates = patchCandidates
            .Where(candidate => NeedsRemovedMatches(candidate.File))
            .Select(candidate => (candidate.Commit.Hash, candidate.File.Path))
            .ToHashSet();
        List<GitFileSearchResult> results = [];
        foreach ((GitCommit commit, GitChangedFile file, string blob) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<GitFileSearchMatch> matches = matchesByBlob.TryGetValue(blob, out List<GitFileSearchMatch>? blobMatches)
                ? blobMatches : [];
            if (candidates.Contains((commit.Hash, file.Path)))
            {
                DiffResult diff = await GetCommitDiffAsync(repository, commit, file, cancellationToken).ConfigureAwait(false);
                matches = AddPatchMatches(diff, file, query, includeAddedLines: false, matches, cancellationToken);
            }

            if (matches.Count > 0)
            {
                results.Add(new GitFileSearchResult(commit, file, matches));
            }
        }

        return results;
    }

    private async Task<List<(GitCommit Commit, GitChangedFile File, string Blob)>> ReadHistoryFilesAsync(
        RepositoryInfo repository, string commitHash, string? patchQuery, CancellationToken cancellationToken)
    {
        List<string> arguments = ["log", "--topo-order", "--raw", "-z", "--no-abbrev",
            "--no-color", "--no-ext-diff", "--no-textconv", "--find-renames", "--root",
            "--diff-merges=first-parent", "--format=%x00commit %H%x09%P%x09%s%x00"];
        if (patchQuery is not null)
        {
            string literalPattern = Regex.Replace(patchQuery, @"[\\.^$|?*+(){}\[\]]", @"\$0");
            arguments.AddRange(["-i", "-G", literalPattern]);
            AddWhitespaceOption(arguments);
        }

        arguments.AddRange([commitHash, "--"]);
        string log = await RunGitAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
        return ReadHistoryFiles(log, cancellationToken);
    }

    private static bool NeedsRemovedMatches(GitChangedFile file) =>
        file.Status is not ("Added" or "Deleted");

    private static IReadOnlyList<GitFileSearchMatch> AddPatchMatches(
        DiffResult diff, GitChangedFile file, string query, bool includeAddedLines,
        IReadOnlyList<GitFileSearchMatch> fileMatches, CancellationToken cancellationToken)
    {
        List<GitFileSearchMatch> patchMatches = [];
        for (int index = 0; index < diff.Lines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiffLine line = diff.Lines[index];
            if ((line.Kind == DiffLineKind.Removed || (includeAddedLines && line.Kind == DiffLineKind.Added))
                && (!file.IsConflicted || !GitConflictMarkerDetector.IsMarker(line.Text)))
            {
                AddMatches(patchMatches, query, line.Text, index, isDiffRow: true);
            }
        }

        if (patchMatches.Count == 0) { return fileMatches; }
        if (fileMatches.Count == 0) { return patchMatches; }

        return fileMatches.Concat(patchMatches)
            .OrderBy(match => match.IsDiffRow ? diff.Lines[match.Line].SourceLineNumber ?? int.MaxValue : match.Line)
            .ThenByDescending(match => match.IsDiffRow)
            .ToArray();
    }

    private static List<(GitCommit Commit, GitChangedFile File, string Blob)> ReadHistoryFiles(
        string log, CancellationToken cancellationToken)
    {
        List<(GitCommit Commit, GitChangedFile File, string Blob)> files = [];
        GitCommit? commit = null;
        string[] fields = log.Split('\0');
        for (int index = 0; index < fields.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string field = fields[index].TrimStart('\r', '\n');
            if (field.StartsWith("commit ", StringComparison.Ordinal))
            {
                string[] commitFields = field[7..].Split('\t', 3);
                if (commitFields.Length == 3)
                {
                    string hash = commitFields[0];
                    commit = new GitCommit(hash, hash[..7], "", "", null, commitFields[2], "",
                        parentHashes: commitFields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }

                continue;
            }

            if (commit is null || !field.StartsWith(':') || index + 1 >= fields.Length) { continue; }
            string[] metadata = field[1..].Split(' ');
            if (metadata.Length != 5 || metadata[4].Length == 0) { continue; }

            char status = metadata[4][0];
            string path = fields[++index];
            string? previousPath = null;
            if (status is 'R' or 'C' && index + 1 < fields.Length)
            {
                previousPath = path;
                path = fields[++index];
            }

            if (metadata[0] is "160000" or "120000" || metadata[1] is "160000" or "120000") { continue; }
            string statusName = status switch
            {
                'A' => "Added",
                'D' => "Deleted",
                'R' => "Renamed",
                'C' => "Copied",
                _ => "Modified"
            };
            string blob = status == 'D' ? metadata[2] : metadata[3];
            files.Add((commit, new GitChangedFile(path, statusName, previousPath: previousPath), blob));
        }

        return files;
    }

    private static bool CanSearch(string query) => query.Length > 0 && query.AsSpan().IndexOfAny('\r', '\n', '\0') < 0;

    private static void AddMatches(
        List<GitFileSearchMatch> matches, string query, string text, int line, bool isDiffRow = false)
    {
        int column = 0;
        while ((column = text.IndexOf(query, column, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            matches.Add(new GitFileSearchMatch(line, column, query.Length, isDiffRow));
            column += query.Length;
        }
    }
}
