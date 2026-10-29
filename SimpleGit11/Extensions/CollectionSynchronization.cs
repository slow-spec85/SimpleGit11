using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SimpleGit11.Extensions;

// Reconcile keyed snapshots without resetting controls bound to their collections.
public static class CollectionSynchronization
{
    public static void SynchronizeWith<T, TKey>(this ObservableCollection<T> target,
        IEnumerable<T> source, Func<T, TKey> key, Func<T, T, bool>? equivalent = null)
        where T : class where TKey : notnull
    {
        IReadOnlyList<T> items = ReuseItems(target, source, key, equivalent);
        HashSet<TKey> keys = items.Select(key).ToHashSet();
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (!keys.Contains(key(target[index]))) { target.RemoveAt(index); }
        }
        for (int index = 0; index < items.Count; index++)
        {
            T item = items[index];
            if (index < target.Count && EqualityComparer<TKey>.Default.Equals(key(target[index]), key(item)))
            {
                if (!ReferenceEquals(target[index], item)) { target[index] = item; }
                continue;
            }
            int existingIndex = -1;
            for (int candidate = index + 1; candidate < target.Count; candidate++)
            {
                if (EqualityComparer<TKey>.Default.Equals(key(target[candidate]), key(item)))
                {
                    existingIndex = candidate;
                    break;
                }
            }
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
                if (!ReferenceEquals(target[index], item)) { target[index] = item; }
            }
            else { target.Insert(index, item); }
        }
    }

    public static IReadOnlyList<T> ReuseItems<T, TKey>(IReadOnlyList<T> existing,
        IEnumerable<T> source, Func<T, TKey> key, Func<T, T, bool>? equivalent = null)
        where T : class where TKey : notnull
    {
        Dictionary<TKey, T> byKey = existing.ToDictionary(key);
        T[] items = source.Select(item => byKey.TryGetValue(key(item), out T? previous)
            && (ReferenceEquals(previous, item) || equivalent?.Invoke(previous, item) == true)
                ? previous : item).ToArray();
        return existing.SequenceEqual(items, ReferenceEqualityComparer.Instance) ? existing : items;
    }
}
