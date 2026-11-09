using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Represents one fixed-size terminal slot with no native item ownership.</summary>
public sealed class TerminalSlotView
{
    internal TerminalSlotView(int index, TerminalEntry? entry, int cellPixels)
    {
        Index = index;
        Entry = entry;
        if (entry == null)
        {
            return;
        }

        var maximum = Math.Max(entry.SpriteWidth, entry.SpriteHeight);
        SpriteWidthPixels = Math.Max(1, cellPixels * entry.SpriteWidth / maximum);
        SpriteHeightPixels = Math.Max(1, cellPixels * entry.SpriteHeight / maximum);
        QuantityBadge = TerminalQuantityFormatter.Format(entry.Amount);
    }

    /// <summary>Gets the stable zero-based slot index.</summary>
    public int Index { get; }

    /// <summary>Gets the display entry, or null for an intentionally empty slot.</summary>
    public TerminalEntry? Entry { get; }

    /// <summary>Gets the equal-aspect fitted width for the fixed cell.</summary>
    public int SpriteWidthPixels { get; }

    /// <summary>Gets the equal-aspect fitted height for the fixed cell.</summary>
    public int SpriteHeightPixels { get; }

    /// <summary>Gets the quantity badge text, or an empty string for an empty slot.</summary>
    public string QuantityBadge { get; } = string.Empty;
}

/// <summary>Describes the terminal data state independently from the native window.</summary>
public enum TerminalDisplayState
{
    /// <summary>The terminal has not received a storage snapshot.</summary>
    Loading,

    /// <summary>The current grid is unavailable.</summary>
    Offline,

    /// <summary>The current grid is online but has no matching items.</summary>
    Empty,

    /// <summary>The current grid has matching items.</summary>
    Ready
}

/// <summary>Formats quantities for a compact badge without changing the authoritative value.</summary>
public static class TerminalQuantityFormatter
{
    /// <summary>Formats a quantity with compact suffixes only above four digits.</summary>
    public static string Format(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (amount < 10_000)
        {
            return amount.ToString(CultureInfo.InvariantCulture);
        }

        if (amount < 1_000_000)
        {
            return $"{amount / 1000.0:0.#}k";
        }

        if (amount < 1_000_000_000)
        {
            return $"{amount / 1_000_000.0:0.#}m";
        }

        return $"{amount / 1_000_000_000.0:0.#}b";
    }
}

/// <summary>Immutable page snapshot consumed by a platform renderer.</summary>
public sealed class StorageScreenPage
{
    internal StorageScreenPage(TerminalDisplayState state, string searchText, int page, int pageCount,
        IReadOnlyList<TerminalSlotView> slots)
    {
        State = state;
        SearchText = searchText;
        Page = page;
        PageCount = pageCount;
        Slots = new ReadOnlyCollection<TerminalSlotView>(slots.ToArray());
    }

    /// <summary>Gets the state represented by this page.</summary>
    public TerminalDisplayState State { get; }

    /// <summary>Gets the current normalized search query.</summary>
    public string SearchText { get; }

    /// <summary>Gets the zero-based current page.</summary>
    public int Page { get; }

    /// <summary>Gets the total number of pages, with one page for an empty result.</summary>
    public int PageCount { get; }

    /// <summary>Gets exactly the configured number of fixed slots.</summary>
    public IReadOnlyList<TerminalSlotView> Slots { get; }
}

/// <summary>Pure terminal screen model for filtering, sorting, paging, and fixed slot layout.</summary>
public sealed class StorageScreen
{
    /// <summary>Creates a fixed-layout screen model.</summary>
    public StorageScreen(int columns = 7, int rows = 4, int cellPixels = 32)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        if (cellPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellPixels));
        }

        Columns = columns;
        Rows = rows;
        CellPixels = cellPixels;
        _pageSize = checked(columns * rows);
        _page = 0;
    }

    private readonly int _pageSize;
    private readonly List<TerminalEntry> _entries = new();
    private string _searchText = string.Empty;
    private int _page;
    private TerminalDisplayState _baseState = TerminalDisplayState.Loading;

    /// <summary>Gets the fixed number of columns.</summary>
    public int Columns { get; }

    /// <summary>Gets the fixed number of rows.</summary>
    public int Rows { get; }

    /// <summary>Gets the fixed sprite cell size in logical pixels.</summary>
    public int CellPixels { get; }

    /// <summary>Replaces the current display snapshot without retaining platform objects.</summary>
    public void SetEntries(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null).OrderBy(entry => entry.DisplayName,
            StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Key));
        _baseState = TerminalDisplayState.Ready;
        _page = 0;
    }

    /// <summary>Marks the view as loading and clears stale entries.</summary>
    public void SetLoading()
    {
        _entries.Clear();
        _baseState = TerminalDisplayState.Loading;
        _page = 0;
    }

    /// <summary>Replaces the display snapshot in the caller's stable ordering.</summary>
    public void SetEntriesInOrder(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null));
        _baseState = TerminalDisplayState.Ready;
        _page = 0;
    }

    /// <summary>Replaces network entries while retaining the current query and valid page.</summary>
    public void UpdateEntries(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var page = _page;
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null).OrderBy(entry => entry.DisplayName,
            StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Key));
        _baseState = TerminalDisplayState.Ready;
        _page = Math.Min(page, GetPageCount() - 1);
    }

    /// <summary>Updates entries in caller order while retaining the current query and valid page.</summary>
    public void UpdateEntriesInOrder(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var page = _page;
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null));
        _baseState = TerminalDisplayState.Ready;
        _page = Math.Min(page, GetPageCount() - 1);
    }

    /// <summary>Marks the view offline and clears stale entries.</summary>
    public void SetOffline()
    {
        _entries.Clear();
        _baseState = TerminalDisplayState.Offline;
        _page = 0;
    }

    /// <summary>Updates the search query and resets the page to the first result.</summary>
    public void SetSearchText(string? searchText)
    {
        _searchText = searchText?.Trim() ?? string.Empty;
        _page = 0;
    }

    /// <summary>Moves to an explicitly clamped page.</summary>
    public void SetPage(int page)
    {
        _page = Math.Max(0, Math.Min(page, GetPageCount() - 1));
    }

    /// <summary>Moves one page forward when a next page exists.</summary>
    public void NextPage() => SetPage(_page + 1);

    /// <summary>Moves one page backward when a previous page exists.</summary>
    public void PreviousPage() => SetPage(_page - 1);

    /// <summary>Gets the current page snapshot with fixed slot count and stable ordering.</summary>
    public StorageScreenPage GetPage()
    {
        var filtered = _entries.Where(entry => entry.Matches(_searchText)).ToArray();
        var pageCount = Math.Max(1, (filtered.Length + _pageSize - 1) / _pageSize);
        _page = Math.Min(_page, pageCount - 1);
        var pageEntries = filtered.Skip(_page * _pageSize).Take(_pageSize).ToArray();
        var slots = new List<TerminalSlotView>(_pageSize);
        for (var index = 0; index < _pageSize; index++)
        {
            slots.Add(new TerminalSlotView(index,
                index < pageEntries.Length ? pageEntries[index] : null, CellPixels));
        }

        var state = _baseState == TerminalDisplayState.Ready && filtered.Length == 0
            ? TerminalDisplayState.Empty
            : _baseState;
        return new StorageScreenPage(state, _searchText, _page, pageCount, slots);
    }

    /// <summary>Gets the current filtered page count.</summary>
    public int GetPageCount()
    {
        var count = _entries.Count(entry => entry.Matches(_searchText));
        return Math.Max(1, (count + _pageSize - 1) / _pageSize);
    }
}
