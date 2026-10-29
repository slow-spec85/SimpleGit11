using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SimpleGit11.Extensions;
using SimpleGit11.Models;

namespace SimpleGit11.Tests.Presentation;

[TestClass]
public sealed class CollectionSynchronizationTests
{
    [TestMethod]
    public void UnchangedSnapshotRetainsObjectsWithoutNotifications()
    {
        GitRemote original = new("origin", "old", "old");
        ObservableCollection<GitRemote> items = [original];
        int notifications = 0;
        items.CollectionChanged += (_, _) => notifications++;
        items.SynchronizeWith([new GitRemote("origin", "old", "old")], item => item.Name,
            (previous, current) => previous.HasSameSnapshot(current));
        Assert.AreSame(original, items.Single());
        Assert.AreEqual(0, notifications);
    }

    [TestMethod]
    public void ChangedSnapshotMovesReplacesAddsAndRemovesWithoutReset()
    {
        GitRemote origin = new("origin", "old", "old");
        GitRemote keep = new("keep", "url", "url");
        ObservableCollection<GitRemote> items = [origin, new("remove", "", ""), keep];
        List<NotifyCollectionChangedAction> actions = [];
        items.CollectionChanged += (_, args) => actions.Add(args.Action);
        items.SynchronizeWith([new GitRemote("keep", "url", "url"), new("origin", "new", "new"), new("added", "", "")],
            item => item.Name, (previous, current) => previous.HasSameSnapshot(current));
        Assert.AreSame(keep, items[0]);
        Assert.AreEqual("new", items[1].FetchUrl);
        Assert.AreEqual("added", items[2].Name);
        Assert.IsFalse(actions.Contains(NotifyCollectionChangedAction.Reset));
        CollectionAssert.AreEquivalent(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Move,
            NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Add }, actions);
    }
}
