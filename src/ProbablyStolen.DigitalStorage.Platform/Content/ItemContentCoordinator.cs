using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal enum ItemContentResult { Unchanged, Changed, Skipped, Failed }

internal readonly struct ItemContentBatchResult
{
    internal ItemContentBatchResult(int changed, int unchanged, int skipped, int failed)
    {
        Changed = changed;
        Unchanged = unchanged;
        Skipped = skipped;
        Failed = failed;
    }

    internal int Changed { get; }
    internal int Unchanged { get; }
    internal int Skipped { get; }
    internal int Failed { get; }
}

internal sealed class ItemContentCoordinator<T> where T : class
{
    private const int PruneInterval = 64;
    private readonly List<WeakReference<T>> _tracked = new();
    private ConditionalWeakTable<T, Marker> _membership = new();
    private readonly Func<T, bool, ItemContentResult> _apply;
    private readonly Action _changed;
    private readonly Action<Exception> _error;
    private int _nextPruneAt = PruneInterval;
    private bool _disposed;

    internal ItemContentCoordinator(Func<T, bool, ItemContentResult> apply, Action changed, Action<Exception> error)
    {
        _apply = apply;
        _changed = changed;
        _error = error;
    }

    internal int TrackedCount => _tracked.Count;

    internal void Reset()
    {
        _tracked.Clear();
        _membership = new ConditionalWeakTable<T, Marker>();
        _nextPruneAt = PruneInterval;
    }

    internal void Track(T item)
    {
        if (_disposed || _membership.TryGetValue(item, out _)) return;
        _membership.Add(item, new Marker());
        _tracked.Add(new WeakReference<T>(item));
        if (_tracked.Count >= _nextPruneAt)
        {
            PruneDead();
            _nextPruneAt = _tracked.Count + PruneInterval;
        }
    }

    internal ItemContentBatchResult Apply(IEnumerable<T> items, bool textOnly, bool forceNotification = false)
    {
        if (_disposed) return new ItemContentBatchResult(0, 0, 0, 0);
        var seen = new HashSet<T>(ReferenceComparer.Instance);
        var changed = 0;
        var unchanged = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var item in items)
        {
            if (item == null || !seen.Add(item)) continue;
            Track(item);
            ItemContentResult result;
            try { result = _apply(item, textOnly); }
            catch (Exception error)
            {
                try { _error(error); }
                catch { }
                result = ItemContentResult.Failed;
            }
            switch (result)
            {
                case ItemContentResult.Changed: changed++; break;
                case ItemContentResult.Unchanged: unchanged++; break;
                case ItemContentResult.Skipped: skipped++; break;
                default: failed++; break;
            }
        }
        if (changed > 0 || failed > 0 || forceNotification) _changed();
        return new ItemContentBatchResult(changed, unchanged, skipped, failed);
    }

    internal ItemContentBatchResult ApplyTracked(bool textOnly, IEnumerable<T>? additional = null)
    {
        var items = new List<T>();
        for (var index = _tracked.Count - 1; index >= 0; index--)
        {
            if (_tracked[index].TryGetTarget(out var item)) items.Add(item);
            else _tracked.RemoveAt(index);
        }
        _nextPruneAt = _tracked.Count + PruneInterval;
        if (additional != null) items.AddRange(additional);
        return Apply(items, textOnly);
    }

    internal void Dispose()
    {
        _disposed = true;
        Reset();
    }

    private void PruneDead()
    {
        for (var index = _tracked.Count - 1; index >= 0; index--)
            if (!_tracked[index].TryGetTarget(out _)) _tracked.RemoveAt(index);
    }

    private sealed class Marker { }

    private sealed class ReferenceComparer : IEqualityComparer<T>
    {
        internal static readonly ReferenceComparer Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
