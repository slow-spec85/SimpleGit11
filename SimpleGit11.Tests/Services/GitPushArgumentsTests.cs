using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;
using SimpleGit11.Services;

namespace SimpleGit11.Tests.Services;

[TestClass]
public sealed class GitPushArgumentsTests
{
    [TestMethod]
    public void Create_RegularBatch_UsesSinglePushWithAllReferences()
    {
        GitPushRequest request = new(
            "origin",
            [
                new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "main"),
                new GitPushReferenceUpdate(GitPushReferenceKind.Tag, "v1.0")
            ],
            GitPushMode.Regular);

        IReadOnlyList<string> arguments = GitPushArguments.Create(request);

        CollectionAssert.AreEqual(
            new[]
            {
                "push",
                "--progress",
                "origin",
                "refs/heads/main:refs/heads/main",
                "refs/tags/v1.0:refs/tags/v1.0"
            },
            arguments.ToArray());
    }

    [TestMethod]
    public void Create_AtomicBatch_AddsAtomicOption()
    {
        GitPushRequest request = new(
            "origin",
            [new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "main")],
            GitPushMode.Atomic);

        IReadOnlyList<string> arguments = GitPushArguments.Create(request);

        CollectionAssert.AreEqual(
            new[]
            {
                "push",
                "--progress",
                "--atomic",
                "origin",
                "refs/heads/main:refs/heads/main"
            },
            arguments.ToArray());
    }

    [TestMethod]
    public void Create_ForcedBranch_UsesScopedForceWithLease()
    {
        GitPushRequest request = new(
            "public",
            [
                new GitPushReferenceUpdate(
                    GitPushReferenceKind.Branch,
                    "release",
                    ForceWithLease: true),
                new GitPushReferenceUpdate(GitPushReferenceKind.Tag, "v2.0")
            ],
            GitPushMode.Atomic);

        IReadOnlyList<string> arguments = GitPushArguments.Create(request);

        CollectionAssert.AreEqual(
            new[]
            {
                "push",
                "--progress",
                "--atomic",
                "--force-with-lease=refs/heads/release",
                "public",
                "refs/heads/release:refs/heads/release",
                "refs/tags/v2.0:refs/tags/v2.0"
            },
            arguments.ToArray());
    }

    [TestMethod]
    public void Create_ForcedTag_Throws()
    {
        GitPushRequest request = new(
            "origin",
            [
                new GitPushReferenceUpdate(
                    GitPushReferenceKind.Tag,
                    "v1.0",
                    ForceWithLease: true)
            ],
            GitPushMode.Atomic);

        Assert.ThrowsExactly<ArgumentException>(() => GitPushArguments.Create(request));
    }

    [TestMethod]
    public void Create_DuplicateReference_Throws()
    {
        GitPushRequest request = new(
            "origin",
            [
                new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "main"),
                new GitPushReferenceUpdate(GitPushReferenceKind.Branch, "main")
            ],
            GitPushMode.Regular);

        Assert.ThrowsExactly<ArgumentException>(() => GitPushArguments.Create(request));
    }
}
