using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// The scoped external access of the public API. Cell page removal callbacks (<see cref="CellPages"/>) ask
/// <see cref="AllowsRemoval"/>; it answers true only while a scope is open and only for roots of cells in online
/// networks, so another mod's scan that respects <c>GameItem.MayRemove</c>/<c>MaxNumRemove</c> finds them. Moving the
/// roots stays with <see cref="IStorage.ExtractRoot"/>. Cells changed during a scope are compacted when it closes, or
/// when the running batch ends; outside both they are compacted at once.
/// </summary>
/// <remarks>
/// Page callbacks are plain delegates on each page, so the open scope is held in a static field. Scopes are main
/// thread only, nest, and live for one frame: a scope still open in a later frame was leaked (its closing call never
/// ran), so removal checks deny and close it, and the next <see cref="Open"/> resets it instead of nesting.
/// </remarks>
internal sealed class ExternalAccessScope : IExternalAccess
{
    private static ExternalAccessScope? _active;

    private readonly Func<GameItem, GameItem?> _cellOfStored;
    private readonly Func<GameItem, bool> _isCellOnline;
    private readonly Action<GameItem> _compact;
    private readonly Func<int> _frame;
    private readonly Func<bool> _isMainThread;
    private readonly Action<string> _log;
    private readonly Dictionary<GameItem, bool> _online = new();
    private readonly List<GameItem> _touched = new();
    private readonly List<GameItem> _pendingCompaction = new();
    private int _depth;
    private int _openedFrame;
    private int _generation;
    private bool _batching;
    private int _batchGeneration;
    private IActionSource? _batchSource;

    /// <summary>Creates the scope service.</summary>
    /// <param name="cellOfStored">The cell whose page holds a root, or null when the root is not on a cell page.</param>
    /// <param name="isCellOnline">Whether a cell sits in a drive of an online network.</param>
    /// <param name="compact">Removes empty pages of a cell; must not throw.</param>
    /// <param name="frame">The current frame number.</param>
    /// <param name="isMainThread">Whether the caller runs on the main thread.</param>
    /// <param name="log">Writes a warning.</param>
    internal ExternalAccessScope(Func<GameItem, GameItem?> cellOfStored, Func<GameItem, bool> isCellOnline,
        Action<GameItem> compact, Func<int> frame, Func<bool> isMainThread, Action<string> log)
    {
        _cellOfStored = cellOfStored ?? throw new ArgumentNullException(nameof(cellOfStored));
        _isCellOnline = isCellOnline ?? throw new ArgumentNullException(nameof(isCellOnline));
        _compact = compact ?? throw new ArgumentNullException(nameof(compact));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public bool IsOpen => _depth > 0;

    /// <inheritdoc />
    public bool IsBatching => _batching;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The caller is not on the main thread.</exception>
    public IDisposable Batch(IActionSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!_isMainThread()) throw new InvalidOperationException("External access batches are main thread only.");
        if (_batching)
        {
            // Batches do not nest, so a running one was never ended (for example its caller's coroutine stopped).
            _log($"Digital Storage: an external access batch of {Describe(_batchSource)} was never ended; ending it.");
            EndBatch(_batchGeneration);
        }

        _batching = true;
        _batchSource = source;
        _batchGeneration++;
        return new Handle(this, _batchGeneration, batch: true);
    }

    /// <summary>Gets the source of the outermost open scope, or null.</summary>
    internal IActionSource? Source { get; private set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The caller is not on the main thread.</exception>
    public IDisposable Open(IActionSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!_isMainThread()) throw new InvalidOperationException("External access scopes are main thread only.");

        if (_depth > 0 && _frame() != _openedFrame)
        {
            _log($"Digital Storage: an external access scope of {Describe(Source)} was never closed; resetting it.");
            Close(compact: true);
        }

        if (_depth == 0)
        {
            if (!_batching) CompactPending();
            _online.Clear();
            _touched.Clear();
            _openedFrame = _frame();
            Source = source;
            _active = this;
        }

        _depth++;
        return new Handle(this, _generation, batch: false);
    }

    /// <summary>
    /// Answers the cell page removal callbacks: true only inside an open scope and only for a root of a cell in an
    /// online network. Outside a scope the answer is false, as before the API existed.
    /// </summary>
    internal static bool AllowsRemoval(GameItem item) => _active is { } scope && scope.Allows(item);

    /// <summary>
    /// Records a cell changed by an API call. Inside a scope its compaction waits for the scope to close, inside a
    /// batch for the batch to end; outside both it runs now.
    /// </summary>
    internal void Touched(GameItem cell)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (IsOpen)
        {
            if (!_touched.Contains(cell)) _touched.Add(cell);
            // A cell that changed may have left or joined a page; ask again for the next root.
            _online.Remove(cell);
            return;
        }

        if (_batching)
        {
            if (!_pendingCompaction.Contains(cell)) _pendingCompaction.Add(cell);
            return;
        }

        _compact(cell);
    }

    private bool Allows(GameItem item)
    {
        if (!IsOpen || item == null) return false;
        if (_frame() != _openedFrame)
        {
            // The closing call never ran. Deny instead of leaving cell pages removable; compaction waits for the next
            // scope because this runs inside a native removal check.
            _log($"Digital Storage: an external access scope of {Describe(Source)} outlived its frame; closing it.");
            Close(compact: false);
            return false;
        }

        var cell = _cellOfStored(item);
        if (cell == null) return false;
        if (!_online.TryGetValue(cell, out var online))
        {
            try
            {
                online = _isCellOnline(cell);
            }
            catch (Exception error)
            {
                _log($"Digital Storage: network check for external access failed ({error.GetType().Name}).");
                online = false;
            }

            _online[cell] = online;
        }

        return online;
    }

    private void Release(int generation)
    {
        // Handles of a scope that was reset as leaked belong to an older generation and must not close a newer one.
        if (generation != _generation || _depth == 0) return;
        _depth--;
        if (_depth == 0) Close(compact: true);
    }

    private void Close(bool compact)
    {
        _depth = 0;
        _generation++;
        Source = null;
        if (ReferenceEquals(_active, this)) _active = null;
        var touched = _touched.ToArray();
        _touched.Clear();
        _online.Clear();
        foreach (var cell in touched)
        {
            if (!_pendingCompaction.Contains(cell)) _pendingCompaction.Add(cell);
        }

        if (compact && !_batching) CompactPending();
    }

    /// <summary>
    /// Forgets cells waiting for compaction; called when a game loads, because those cells belong to the previous game.
    /// Open scopes and the running batch are left to their callers.
    /// </summary>
    internal void DiscardPending()
    {
        _pendingCompaction.Clear();
        _touched.Clear();
        _online.Clear();
    }

    private void EndBatch(int generation)
    {
        // A handle of a batch that a newer batch already ended must not end the newer one.
        if (generation != _batchGeneration || !_batching) return;
        _batching = false;
        _batchSource = null;
        if (!IsOpen) CompactPending();
    }

    private void CompactPending()
    {
        var cells = _pendingCompaction.ToArray();
        _pendingCompaction.Clear();
        foreach (var cell in cells)
        {
            try
            {
                _compact(cell);
            }
            catch (Exception error)
            {
                _log($"Digital Storage: compaction after external access failed ({error.GetType().Name}).");
            }
        }
    }

    private static string Describe(IActionSource? source) =>
        source?.ModId ?? source?.MachineId ?? source?.PlayerName ?? "an unknown caller";

    private sealed class Handle : IDisposable
    {
        private readonly int _generation;
        private readonly bool _batch;
        private ExternalAccessScope? _owner;

        internal Handle(ExternalAccessScope owner, int generation, bool batch)
        {
            _owner = owner;
            _generation = generation;
            _batch = batch;
        }

        public void Dispose()
        {
            // A second Dispose of the same handle must not close an outer scope or a later batch.
            var owner = _owner;
            _owner = null;
            if (owner == null) return;
            if (_batch) owner.EndBatch(_generation);
            else owner.Release(_generation);
        }
    }
}
