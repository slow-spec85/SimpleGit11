using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Models;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitDiffService : IGitDiffService
{
    private readonly ISettingsService _settingsService;
    private readonly IGitCommandRunner _commandRunner;
    private readonly ITextFileService? _textFileService;

    public GitDiffService(
        ISettingsService settingsService,
        IGitCommandRunner? commandRunner = null,
        ITextFileService? textFileService = null)
    {
        _settingsService = settingsService;
        _commandRunner = commandRunner ?? new GitCommandRunner();
        _textFileService = textFileService;
    }

    public async Task<DiffResult> GetDiffAsync(RepositoryInfo repository, GitChangedFile statusEntry)
    {
        if (statusEntry.State == GitChangeState.Conflicted)
        {
            return await GetConflictFileAsync(repository, statusEntry);
        }

        if (statusEntry.Status == "Untracked")
        {
            return new DiffResult([], false, true, "Diff is not available until the new file is staged.");
        }

        var output = await RunGitDiffAsync(repository, statusEntry);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new DiffResult([], false, true, "No textual diff is available for this file.");
        }

        if (output.Contains("Binary files ") || output.Contains("GIT binary patch"))
        {
            return new DiffResult([], true, true, "Binary file diff cannot be displayed.");
        }

        return new DiffResult(GitDiffParser.Parse(output), false, false, "");
    }

    public async Task<string> GetFullFileTextAsync(RepositoryInfo repository, GitChangedFile statusEntry)
    {
        if (_textFileService is not null)
        {
            try
            {
                return (await _textFileService.ReadAsync(repository, statusEntry.Path)).Text;
            }
            catch (FileNotFoundException)
            {
                return "";
            }
        }

        var filePath = GetSafeFilePath(repository, statusEntry.Path);
        if (!File.Exists(filePath))
        {
            return "";
        }

        return await File.ReadAllTextAsync(filePath, Encoding.UTF8);
    }

    public async Task<DiffResult> GetCommitDiffAsync(
        RepositoryInfo repository,
        GitCommit commit,
        GitChangedFile changedFile)
    {
        var arguments = new List<string>
        {
            commit.HasDiffBaseRevision ? "diff" : "show"
        };
        if (!commit.HasDiffBaseRevision)
        {
            arguments.Add("--format=");
        }
        arguments.Add("--find-renames");

        AddWhitespaceOption(arguments);
        if (commit.HasDiffBaseRevision)
        {
            arguments.Add(commit.DiffBaseRevision);
        }
        arguments.Add(commit.Hash);
        arguments.Add("--");
        arguments.Add(changedFile.Path);

        var output = await RunGitAsync(repository, arguments);
        if (string.IsNullOrWhiteSpace(output))
        {
            return new DiffResult([], false, true, "No textual diff is available for this commit.");
        }

        if (output.Contains("Binary files ") || output.Contains("GIT binary patch"))
        {
            return new DiffResult([], true, true, "Binary file diff cannot be displayed.");
        }

        return new DiffResult(GitDiffParser.Parse(output), false, false, "");
    }

    public async Task<string> GetCommitFileTextAsync(
        RepositoryInfo repository,
        GitCommit commit,
        GitChangedFile changedFile)
    {
        if (!changedFile.CanShowFileContent)
        {
            return "";
        }

        var revision = changedFile.Status == "Deleted"
            ? $"{(commit.HasDiffBaseRevision ? commit.DiffBaseRevision : commit.Hash + "^")}:{changedFile.Path}"
            : $"{commit.Hash}:{changedFile.Path}";

        try
        {
            return await RunGitAsync(repository, ["show", revision]);
        }
        catch (GitCommandException exception)
        {
            if (changedFile.Status == "Added")
            {
                return "";
            }

            throw new GitCommandException(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Full file content is not available for this commit."
                    : exception.Message,
                exception.ExitCode);
        }
    }

    public async Task RevertChangeAsync(RepositoryInfo repository, GitChangedFile statusEntry, int lineNumber)
    {
        if (lineNumber < 1 || statusEntry.Status == "Untracked" || statusEntry.State != GitChangeState.Unstaged)
        {
            return;
        }

        TextFileDocument? document = null;
        string filePath;
        string currentText;
        if (_textFileService is not null)
        {
            try
            {
                document = await _textFileService.ReadAsync(repository, statusEntry.Path);
            }
            catch (FileNotFoundException)
            {
                return;
            }

            filePath = document.Path;
            currentText = document.Text;
        }
        else
        {
            filePath = GetSafeFilePath(repository, statusEntry.Path);
            if (!File.Exists(filePath))
            {
                return;
            }

            currentText = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        }

        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        var diff = await RunGitDiffAsync(repository, statusEntry);
        var changeBlock = FindChangeBlock(diff, lineNumber);
        if (changeBlock is null)
        {
            return;
        }

        var currentLines = SplitPreservingLineEndings(currentText).ToList();
        var startIndex = System.Math.Max(1, changeBlock.NewStart) - 1;
        if (startIndex < 0 || startIndex > currentLines.Count)
        {
            return;
        }

        var lineEndingSample = startIndex < currentLines.Count
            ? currentLines[startIndex]
            : currentLines.LastOrDefault() ?? currentText;
        var replacementLines = changeBlock.OldLines
            .Select(line => MatchLineEnding(line, lineEndingSample))
            .ToList();
        var removeCount = System.Math.Min(changeBlock.NewLineCount, currentLines.Count - startIndex);
        currentLines.RemoveRange(startIndex, removeCount);
        currentLines.InsertRange(startIndex, replacementLines);

        string updatedText = string.Concat(currentLines);
        if (_textFileService is not null && document is not null)
        {
            await _textFileService.WriteAsync(document, updatedText);
        }
        else
        {
            await File.WriteAllTextAsync(filePath, updatedText, Encoding.UTF8);
        }
    }

    private static DiffChangeBlock? FindChangeBlock(string diff, int lineNumber)
    {
        var blocks = ParseChangeBlocks(diff);
        return blocks.FirstOrDefault(block =>
            block.NewLineCount > 0
                ? lineNumber >= block.NewStart && lineNumber < block.NewStart + block.NewLineCount
                : lineNumber == System.Math.Max(1, block.NewStart));
    }

    private static IReadOnlyList<DiffChangeBlock> ParseChangeBlocks(string diff)
    {
        var blocks = new List<DiffChangeBlock>();
        var oldLineNumber = 0;
        var newLineNumber = 0;
        var blockStart = 0;
        var blockNewLineCount = 0;
        bool blockStarted = false;
        List<string> blockOldLines = [];

        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (TryReadHunk(line, out var oldStart, out var newStart))
            {
                FlushBlock();
                oldLineNumber = oldStart;
                newLineNumber = newStart;
                continue;
            }

            if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                EnsureBlockStarted();
                blockNewLineCount++;
                newLineNumber++;
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                EnsureBlockStarted();
                blockOldLines.Add(line[1..]);
                oldLineNumber++;
            }
            else
            {
                FlushBlock();
                if (!line.StartsWith("diff --git") &&
                    !line.StartsWith("index ") &&
                    !line.StartsWith("---") &&
                    !line.StartsWith("+++") &&
                    !line.StartsWith("\\ No newline "))
                {
                    oldLineNumber++;
                    newLineNumber++;
                }
            }
        }

        FlushBlock();
        return blocks;

        void EnsureBlockStarted()
        {
            if (!blockStarted)
            {
                blockStarted = true;
                blockStart = newLineNumber;
                blockNewLineCount = 0;
                blockOldLines = [];
            }
        }

        void FlushBlock()
        {
            if (!blockStarted)
            {
                return;
            }

            blocks.Add(new DiffChangeBlock(blockStart, blockNewLineCount, blockOldLines));
            blockStarted = false;
            blockStart = 0;
            blockNewLineCount = 0;
            blockOldLines = [];
        }
    }

    private static bool TryReadHunk(string line, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;
        if (!line.StartsWith("@@"))
        {
            return false;
        }

        var headerParts = line.Split(' ');
        if (headerParts.Length < 3)
        {
            return false;
        }

        return TryReadHunkLineNumber(headerParts[1], '-', out oldStart) &&
            TryReadHunkLineNumber(headerParts[2], '+', out newStart);
    }

    private static bool TryReadHunkLineNumber(string part, char prefix, out int lineNumber)
    {
        lineNumber = 0;
        if (part.Length < 2 || part[0] != prefix)
        {
            return false;
        }

        var commaIndex = part.IndexOf(',');
        var value = commaIndex >= 0 ? part[1..commaIndex] : part[1..];
        return int.TryParse(value, out lineNumber);
    }

    private async Task<DiffResult> GetConflictFileAsync(RepositoryInfo repository, GitChangedFile statusEntry)
    {
        if (_textFileService is not null)
        {
            try
            {
                string conflictText = (await _textFileService.ReadAsync(repository, statusEntry.Path)).Text;
                return string.IsNullOrWhiteSpace(conflictText)
                    ? new DiffResult([], false, true, "No textual diff is available for this file.")
                    : new DiffResult(ParseConflictFile(conflictText), false, false, "");
            }
            catch (FileNotFoundException)
            {
                return new DiffResult([], false, true, "Conflict file is not available in the working tree.");
            }
        }

        var filePath = Path.GetFullPath(Path.Combine(repository.Path, statusEntry.Path));

        if (!IsFilePathInsideRepository(repository, filePath) || !File.Exists(filePath))
        {
            return new DiffResult([], false, true, "Conflict file is not available in the working tree.");
        }

        var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DiffResult([], false, true, "No textual diff is available for this file.");
        }

        return new DiffResult(ParseConflictFile(text), false, false, "");
    }

    private async Task<string> RunGitDiffAsync(RepositoryInfo repository, GitChangedFile statusEntry)
    {
        var arguments = new List<string>
        {
            "diff"
        };

        if (statusEntry.State == GitChangeState.Staged)
        {
            arguments.Add("--cached");
        }

        AddWhitespaceOption(arguments);
        arguments.Add("--");
        arguments.Add(statusEntry.Path);

        return await RunGitAsync(repository, arguments);
    }

    private void AddWhitespaceOption(ICollection<string> arguments)
    {
        if (_settingsService.Current.IgnoreWhitespaceInDiff)
        {
            arguments.Add("--ignore-all-space");
        }
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, IReadOnlyList<string> arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(repository.Path, arguments);
        return result.StandardOutput;
    }

    private static string GetSafeFilePath(RepositoryInfo repository, string relativePath)
    {
        return RepositoryPathGuard.GetSafeFilePath(repository.Path, relativePath);
    }

    private static bool IsFilePathInsideRepository(RepositoryInfo repository, string filePath)
    {
        return RepositoryPathGuard.IsPathInsideRepository(repository.Path, filePath);
    }

    private static IEnumerable<string> SplitPreservingLineEndings(string text)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            yield return text[start..(i + 1)];
            start = i + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private static string MatchLineEnding(string sourceLine, string targetLine)
    {
        var targetLineEnding = targetLine.EndsWith("\r\n", System.StringComparison.Ordinal)
            ? "\r\n"
            : targetLine.EndsWith('\n')
                ? "\n"
                : "";
        var trimmedSource = sourceLine.TrimEnd('\r', '\n');
        return trimmedSource + targetLineEnding;
    }

    private static IReadOnlyList<DiffLine> ParseConflictFile(string text)
    {
        var lines = new List<DiffLine>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var kind = GitConflictMarkerDetector.IsMarker(line)
                ? DiffLineKind.ConflictMarker
                : DiffLineKind.Context;
            lines.Add(new DiffLine(line, kind));
        }

        return lines;
    }

    private sealed class DiffChangeBlock
    {
        public DiffChangeBlock(int newStart, int newLineCount, IReadOnlyList<string> oldLines)
        {
            NewStart = newStart;
            NewLineCount = newLineCount;
            OldLines = oldLines;
        }

        public int NewStart { get; }

        public int NewLineCount { get; }

        public IReadOnlyList<string> OldLines { get; }
    }
}
