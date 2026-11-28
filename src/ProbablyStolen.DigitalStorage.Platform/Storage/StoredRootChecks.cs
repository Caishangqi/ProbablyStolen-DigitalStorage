using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Collects stored roots that something changed and checks them together on a later main-thread turn. A root is read
/// only outside native callbacks, and many changes to the roots of one cell cost one check per root and at most one
/// reread of that cell.
/// </summary>
internal sealed class StoredRootChecks<TCell, TRoot> where TCell : class where TRoot : class
{
    private readonly Action<Action> _schedule;
    private readonly Func<TCell, TRoot, bool> _stillMatches;
    private readonly Action<TCell> _forget;
    private readonly Action _changed;
    private Dictionary<TCell, HashSet<TRoot>> _pending = new(Reference<TCell>.Comparer);
    private bool _scheduled;
    private bool _closed;

    /// <param name="schedule">Runs the check on a later main-thread turn.</param>
    /// <param name="stillMatches">Whether the root still has the type its cell's kept contents record.</param>
    /// <param name="forget">Drops the kept contents of a cell whose root no longer matches.</param>
    /// <param name="changed">Runs once after a check dropped any cell's contents.</param>
    internal StoredRootChecks(Action<Action> schedule, Func<TCell, TRoot, bool> stillMatches, Action<TCell> forget,
        Action changed)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _stillMatches = stillMatches ?? throw new ArgumentNullException(nameof(stillMatches));
        _forget = forget ?? throw new ArgumentNullException(nameof(forget));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    internal void Mark(TCell cell, TRoot root)
    {
        if (_closed) return;
        if (!_pending.TryGetValue(cell, out var roots)) _pending[cell] = roots = new HashSet<TRoot>(Reference<TRoot>.Comparer);
        roots.Add(root);
        if (_scheduled) return;
        _scheduled = true;
        _schedule(Flush);
    }

    /// <summary>Stops checking; marks after this are ignored and a scheduled check does nothing.</summary>
    internal void Close()
    {
        _closed = true;
        _pending.Clear();
    }

    internal void Flush()
    {
        _scheduled = false;
        if (_closed || _pending.Count == 0) return;
        var batch = _pending;
        _pending = new Dictionary<TCell, HashSet<TRoot>>(Reference<TCell>.Comparer);
        var dropped = false;
        foreach (var pair in batch)
        {
            foreach (var root in pair.Value)
            {
                bool matches;
                try
                {
                    matches = _stillMatches(pair.Key, root);
                }
                catch (Exception)
                {
                    matches = false;
                }

                if (matches) continue;
                _forget(pair.Key);
                dropped = true;
                break;
            }
        }

        if (dropped) _changed();
    }

    private sealed class Reference<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly Reference<T> Comparer = new();

        public bool Equals(T? left, T? right) => ReferenceEquals(left, right);

        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
}
