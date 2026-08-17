using SimpleGit11.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SimpleGit11.Services.Git.Execution;

namespace SimpleGit11.Services;

public sealed class GitTagService : IGitTagService
{
    private const char UnitSeparator = '\x1f';
    private readonly IGitCommandRunner _commandRunner;

    public GitTagService(IGitCommandRunner? commandRunner = null)
    {
        _commandRunner = commandRunner ?? new GitCommandRunner();
    }

    public async Task<IReadOnlyList<GitTag>> GetLocalTagsAsync(RepositoryInfo repository)
    {
        var output = await RunGitAsync(
            repository,
            "for-each-ref",
            "refs/tags",
            $"--format=%(refname:short){UnitSeparator}%(objectname){UnitSeparator}%(*objectname){UnitSeparator}%(objecttype){UnitSeparator}%(creatordate:local){UnitSeparator}%(contents:subject)");

        return ParseTags(output, isRemote: false);
    }

    public async Task<string?> GetHeadCommitHashAsync(RepositoryInfo repository)
    {
        try
        {
            string headCommitHash = await RunGitAsync(repository, "rev-parse", "--verify", "HEAD^{commit}");
            return headCommitHash.Trim();
        }
        catch (GitCommandException exception) when (exception.ExitCode is 1 or 128)
        {
            return null;
        }
    }

    public Task CreateTagAsync(RepositoryInfo repository, TagCreationRequest request)
    {
        return request.IsAnnotated
            ? RunGitAsync(repository, "tag", "-a", request.TagName, request.StartPointHash, "-m", request.Message)
            : RunGitAsync(repository, "tag", request.TagName, request.StartPointHash);
    }

    public async Task CheckoutTagAsync(RepositoryInfo repository, GitTag tag)
    {
        string startPoint = $"refs/tags/{tag.Name}";
        if (tag.IsRemote)
        {
            if (string.IsNullOrWhiteSpace(tag.RemoteName))
            {
                throw new GitCommandException("The remote for this tag is not available.", -1);
            }

            await RunGitAsync(repository, "fetch", "--no-tags", "--", tag.RemoteName, $"refs/tags/{tag.RemoteTagName}");
            startPoint = "FETCH_HEAD";
        }

        await RunGitAsync(repository, "switch", "--detach", "--", startPoint);
    }

    public Task DeleteTagAsync(RepositoryInfo repository, GitTag tag)
    {
        return RunGitAsync(repository, "tag", "-d", tag.Name);
    }

    private async Task<string> RunGitAsync(RepositoryInfo repository, params string[] arguments)
    {
        GitCommandResult result = await _commandRunner.RunAsync(repository.Path, arguments);
        return result.StandardOutput;
    }

    private static IReadOnlyList<GitTag> ParseTags(string output, bool isRemote)
    {
        var tags = new List<GitTag>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split(UnitSeparator);
            if (fields.Length < 2)
            {
                continue;
            }

            bool isAnnotated = fields.Length > 3 && fields[3].Equals("tag", StringComparison.Ordinal);
            string targetHash = isAnnotated && fields.Length > 2 && !string.IsNullOrWhiteSpace(fields[2])
                ? fields[2]
                : fields[1];

            tags.Add(new GitTag(
                fields[0],
                isRemote,
                isAnnotated,
                targetHash,
                fields.Length > 5 ? fields[5] : "",
                fields.Length > 4 ? ParseDate(fields[4]) : null,
                referenceObjectHash: fields[1]));
        }

        return tags;
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.ParseExact(
            value,
            "ddd MMM d HH:mm:ss yyyy",
            CultureInfo.InvariantCulture);
    }
}
