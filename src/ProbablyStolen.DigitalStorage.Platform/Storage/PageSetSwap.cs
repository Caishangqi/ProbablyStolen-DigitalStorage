using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>The native steps of replacing a cell's page container with one that holds only the kept pages.</summary>
internal interface IPageSetSwapPort<T> where T : class
{
    /// <summary>Gets whether the cell may change pages now (not decoding, not unavailable).</summary>
    bool CanOperate { get; }

    /// <summary>Gets the attached page container.</summary>
    T Current { get; }

    /// <summary>Gets the layout recorded on the cell, or null when it cannot be read.</summary>
    LayoutDescriptor? RecordedLayout { get; }

    /// <summary>Gets the item root count of each current page.</summary>
    IReadOnlyList<int> RootCounts();

    /// <summary>Gets whether the current pages match the recorded layout and every page to remove is empty.</summary>
    bool CanStart(PageCompaction plan);

    /// <summary>Builds an unattached container holding the kept pages in order; it must be retained before it is filled.</summary>
    void Prepare(IReadOnlyList<int> keep, Action<T> retain);

    /// <summary>Makes a container the cell's pages.</summary>
    void Attach(T pages);

    /// <summary>Gets whether a container is attached, matches the layout and kept every root it should hold.</summary>
    bool IsLinked(T pages, LayoutDescriptor layout);

    /// <summary>Writes the layout tag.</summary>
    void WriteLayout(LayoutDescriptor layout);

    /// <summary>Selects the first page of a container.</summary>
    void SelectFirst(T pages);

    /// <summary>Moves every page of the original container back under its rendering.</summary>
    void ReturnPages(T original);

    /// <summary>Destroys a container that is no longer attached, with only empty removed pages left in it.</summary>
    void Release(T pages);
}

/// <summary>
/// Removes empty pages by swapping in a new page container that holds the kept pages. Both containers stay owned until the
/// swap commits or rolls back and the leftovers are released. A successful rollback blocks further compaction of the
/// cell for this session; a failed rollback leaves the cell unavailable and keeps both containers for a retry.
/// Resetting a disassembled housing to its single anchor page is the special case of keeping only page 0.
/// </summary>
internal sealed class PageSetSwap<T> where T : class
{
    private T? _original;
    private T? _replacement;
    private LayoutDescriptor? _originalLayout;
    private LayoutDescriptor? _targetLayout;
    private bool _committed;

    /// <summary>Gets whether a requested or interrupted swap still has to finish.</summary>
    internal bool Pending { get; private set; }

    /// <summary>Gets whether a rollback succeeded; compaction is then not retried this session.</summary>
    internal bool Blocked { get; private set; }

    internal void Request() => Pending = true;

    /// <summary>Gets whether the cell is idle at exactly the anchor page, so a housing may be reassembled.</summary>
    internal bool IsAtAnchor(IPageSetSwapPort<T> port) =>
        !Pending && _original == null && _replacement == null && port.CanOperate && port.RecordedLayout is { } layout &&
        layout.Pages.Count == 1 && port.RootCounts() is { Count: 1 } counts && counts[0] == 0 &&
        port.IsLinked(port.Current, layout);

    /// <summary>Removes the pages the plan drops, or finishes an interrupted swap first.</summary>
    /// <exception cref="InvalidOperationException">The swap cannot start or a step failed; a failed rollback raises an <see cref="AggregateException"/>.</exception>
    internal void Run(IPageSetSwapPort<T> port, PageCompaction plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        Pending = true;
        if (!port.CanOperate) throw new InvalidOperationException("The cell cannot change pages now.");

        if (_original != null)
        {
            if (_committed)
            {
                Complete(port);
                return;
            }

            RollBack(port);
        }

        if (!plan.HasWork)
        {
            Pending = false;
            return;
        }

        if (!port.CanStart(plan)) throw new InvalidOperationException("The pages to remove are not verified empty.");
        _original = port.Current;
        _originalLayout = port.RecordedLayout ?? throw new InvalidOperationException("The layout cannot be read.");
        _targetLayout = plan.After;
        try
        {
            port.Prepare(plan.Kept, pages =>
            {
                if (_replacement != null) throw new InvalidOperationException("A replacement is already retained.");
                _replacement = pages ?? throw new InvalidOperationException("A replacement is required.");
            });
            if (_replacement == null) throw new InvalidOperationException("No replacement was prepared.");
            port.Attach(_replacement);
            if (!port.IsLinked(_replacement, _targetLayout))
                throw new InvalidOperationException("The replacement pages were not linked.");
            port.WriteLayout(_targetLayout);
            port.SelectFirst(_replacement);
            if (!port.IsLinked(_replacement, _targetLayout) || !Equals(port.RecordedLayout, _targetLayout))
                throw new InvalidOperationException("The replacement pages are incomplete.");
            _committed = true;
            Complete(port);
        }
        catch (Exception failure)
        {
            if (!_committed)
            {
                try
                {
                    RollBack(port);
                    Blocked = true;
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("The page swap and its rollback failed.", failure, rollbackFailure);
                }
            }

            throw;
        }
    }

    private void RollBack(IPageSetSwapPort<T> port)
    {
        var original = _original ?? throw new InvalidOperationException("The original pages are unavailable.");
        var layout = _originalLayout ?? throw new InvalidOperationException("The original layout is unavailable.");
        // Prepare already moved the kept pages under the replacement, so they go back before reattaching.
        port.ReturnPages(original);
        port.Attach(original);
        if (!port.IsLinked(original, layout)) throw new InvalidOperationException("The original pages were not restored.");
        port.WriteLayout(layout);
        if (!port.IsLinked(original, layout) || !Equals(port.RecordedLayout, layout))
            throw new InvalidOperationException("The original layout was not restored.");
        if (_replacement != null)
        {
            port.Release(_replacement);
            _replacement = null;
        }

        _original = null;
    }

    private void Complete(IPageSetSwapPort<T> port)
    {
        var layout = _targetLayout ?? throw new InvalidOperationException("The target layout is unavailable.");
        if (_replacement == null || !port.IsLinked(_replacement, layout) || !Equals(port.RecordedLayout, layout))
            throw new InvalidOperationException("The committed replacement is no longer complete.");
        if (_original != null)
        {
            port.Release(_original);
            _original = null;
        }

        _replacement = null;
        _committed = false;
        Pending = false;
    }
}
