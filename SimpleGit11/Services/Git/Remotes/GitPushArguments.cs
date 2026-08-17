using System;
using System.Collections.Generic;
using System.Linq;
using SimpleGit11.Models;

namespace SimpleGit11.Services;

internal static class GitPushArguments
{
    public static IReadOnlyList<string> Create(GitPushRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateReferenceName(request.RemoteName, nameof(request.RemoteName));

        if (request.References is null || request.References.Count == 0)
        {
            throw new ArgumentException(
                "At least one Git reference update is required.",
                nameof(request));
        }

        List<string> arguments = ["push", "--progress"];
        if (request.Mode == GitPushMode.Atomic)
        {
            arguments.Add("--atomic");
        }

        HashSet<(GitPushReferenceKind Kind, string Name)> uniqueReferences = [];
        foreach (GitPushReferenceUpdate reference in request.References)
        {
            ArgumentNullException.ThrowIfNull(reference);
            ValidateReferenceName(reference.Name, nameof(request.References));
            if (!uniqueReferences.Add((reference.Kind, reference.Name)))
            {
                throw new ArgumentException(
                    $"The Git reference '{reference.Name}' is included more than once.",
                    nameof(request));
            }

            if (reference.ForceWithLease)
            {
                if (reference.Kind != GitPushReferenceKind.Branch)
                {
                    throw new ArgumentException(
                        "Force-with-lease can only be used for branch updates.",
                        nameof(request));
                }

                arguments.Add($"--force-with-lease=refs/heads/{reference.Name}");
            }
        }

        arguments.Add(request.RemoteName);
        arguments.AddRange(request.References.Select(CreateRefSpec));
        return arguments;
    }

    private static string CreateRefSpec(GitPushReferenceUpdate reference)
    {
        string namespaceName = reference.Kind switch
        {
            GitPushReferenceKind.Branch => "heads",
            GitPushReferenceKind.Tag => "tags",
            _ => throw new ArgumentOutOfRangeException(
                nameof(reference),
                reference.Kind,
                "Unsupported Git reference kind.")
        };
        return $"refs/{namespaceName}/{reference.Name}:refs/{namespaceName}/{reference.Name}";
    }

    private static void ValidateReferenceName(string referenceName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(referenceName)
            || referenceName.Any(char.IsWhiteSpace)
            || referenceName.Contains("..", StringComparison.Ordinal)
            || referenceName.Contains("@{", StringComparison.Ordinal)
            || referenceName.IndexOfAny(['~', '^', ':', '?', '*', '[', '\\']) >= 0
            || referenceName.StartsWith('.')
            || referenceName.EndsWith('.')
            || referenceName.StartsWith('/')
            || referenceName.EndsWith('/')
            || referenceName.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A valid Git reference name is required.",
                parameterName);
        }
    }
}
