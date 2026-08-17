using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Models;

namespace SimpleGit11.Tests.ViewModels;

[TestClass]
public sealed class GitTagTests
{
    [TestMethod]
    public void WithListMetadataFromMatchingLocalTag_AddsSubjectAndDate()
    {
        DateTime createdDate = new(2026, 8, 17, 12, 30, 0);
        GitTag remoteTag = new(
            "origin/v1.0",
            isRemote: true,
            isAnnotated: true,
            "commit-hash",
            "",
            null,
            "origin",
            "v1.0",
            "tag-object-hash");
        GitTag localTag = new(
            "v1.0",
            isRemote: false,
            isAnnotated: true,
            "commit-hash",
            "Release 1.0",
            createdDate,
            referenceObjectHash: "tag-object-hash");

        GitTag enrichedTag = remoteTag.WithListMetadataFromMatchingLocalTag(localTag);

        Assert.AreEqual("origin/v1.0", enrichedTag.Name);
        Assert.AreEqual("origin", enrichedTag.RemoteName);
        Assert.AreEqual("v1.0", enrichedTag.RemoteTagName);
        Assert.AreEqual("commit-hash", enrichedTag.ObjectHash);
        Assert.AreEqual("tag-object-hash", enrichedTag.ReferenceObjectHash);
        Assert.AreEqual("Release 1.0", enrichedTag.Subject);
        Assert.AreEqual(createdDate, enrichedTag.CreatedDate);
        Assert.IsTrue(enrichedTag.IsRemote);
        Assert.IsTrue(enrichedTag.IsAnnotated);
    }

    [TestMethod]
    public void WithListMetadataFromMatchingLocalTag_DoesNotUseConflictingTag()
    {
        GitTag remoteTag = new(
            "origin/v1.0",
            isRemote: true,
            isAnnotated: true,
            "remote-commit-hash",
            "",
            null,
            "origin",
            "v1.0",
            "remote-tag-object-hash");
        GitTag conflictingLocalTag = new(
            "v1.0",
            isRemote: false,
            isAnnotated: true,
            "local-commit-hash",
            "Local release",
            DateTime.Now,
            referenceObjectHash: "local-tag-object-hash");

        GitTag result = remoteTag.WithListMetadataFromMatchingLocalTag(conflictingLocalTag);

        Assert.AreSame(remoteTag, result);
        Assert.AreEqual("", result.Subject);
        Assert.IsNull(result.CreatedDate);
    }
}
