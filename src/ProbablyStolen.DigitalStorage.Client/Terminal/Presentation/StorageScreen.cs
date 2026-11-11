using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

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

/// <summary>Immutable view snapshot consumed by a platform renderer.</summary>
public sealed class StorageScreenPage
{
    internal StorageScreenPage(TerminalDisplayState state, string searchText, int scrollRow, int maxScroll,
        IReadOnlyList<TerminalSlotView> slots)
    {
        State = state;
        SearchText = searchText;
        ScrollRow = scrollRow;
        MaxScroll = maxScroll;
        Slots = new ReadOnlyCollection<TerminalSlotView>(slots.ToArray());
    }

    /// <summary>Gets the state represented by this view.</summary>
    public TerminalDisplayState State { get; }

    /// <summary>Gets the current raw search query.</summary>
    public string SearchText { get; }

    /// <summary>Gets the zero-based first visible row.</summary>
    public int ScrollRow { get; }

    /// <summary>Gets the largest valid first row; zero when every result fits.</summary>
    public int MaxScroll { get; }

    /// <summary>Gets exactly the configured number of fixed slots.</summary>
    public IReadOnlyList<TerminalSlotView> Slots { get; }
}

/// <summary>Pure terminal screen model for filtering, row scrolling, and fixed slot layout.</summary>
public sealed class StorageScreen
{
    /// <summary>Creates a fixed-column screen model.</summary>
    public StorageScreen(int columns = 7, int rows = 4, int cellPixels = 32, TerminalSearchService? search = null)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cellPixels <= 0) throw new ArgumentOutOfRangeException(nameof(cellPixels));
        _search = search ?? CreateDefaultSearch();
        Columns = columns;
        Rows = rows;
        CellPixels = cellPixels;
    }

    private readonly TerminalSearchService _search;
    private readonly List<TerminalEntry> _entries = new();
    private string _searchText = string.Empty;
    private int _scrollRow;
    private TerminalDisplayState _baseState = TerminalDisplayState.Loading;
    private TerminalEntry[] _filteredEntries = Array.Empty<TerminalEntry>();
    private bool _filterDirty = true;
    private string? _filterText;
    private long _filterConfigurationRevision = -1;
    private StorageScreenPage? _page;

    /// <summary>Gets the fixed number of columns.</summary>
    public int Columns { get; }

    /// <summary>Gets the number of visible rows.</summary>
    public int Rows { get; private set; }

    /// <summary>Gets the fixed sprite cell size in logical pixels.</summary>
    public int CellPixels { get; }

    /// <summary>Gets the rows moved per wheel notch or track click, <c>max(1, rows / 6)</c>.</summary>
    public int PageSize => Math.Max(1, Rows / 6);

    /// <summary>Gets the zero-based first visible row.</summary>
    public int ScrollRow => _scrollRow;

    /// <summary>Changes the visible row count, keeping the first visible row while it stays valid.</summary>
    public void SetRows(int rows)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (Rows == rows) return;
        Rows = rows;
        _page = null;
        ClampScroll();
    }

    /// <summary>Replaces the current display snapshot without retaining platform objects.</summary>
    public void SetEntries(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null).OrderBy(entry => entry.DisplayName,
            StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Key));
        _baseState = TerminalDisplayState.Ready;
        _scrollRow = 0;
        InvalidateEntries();
    }

    /// <summary>Marks the view as loading and clears stale entries.</summary>
    public void SetLoading()
    {
        _entries.Clear();
        _baseState = TerminalDisplayState.Loading;
        _scrollRow = 0;
        InvalidateEntries();
    }

    /// <summary>Replaces the display snapshot in the caller's stable ordering.</summary>
    public void SetEntriesInOrder(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null));
        _baseState = TerminalDisplayState.Ready;
        _scrollRow = 0;
        InvalidateEntries();
    }

    /// <summary>Replaces network entries while retaining the current query and valid scroll row.</summary>
    public void UpdateEntries(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null).OrderBy(entry => entry.DisplayName,
            StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Key));
        _baseState = TerminalDisplayState.Ready;
        InvalidateEntries();
        ClampScroll();
    }

    /// <summary>Updates entries in caller order while retaining the current query and valid scroll row.</summary>
    public void UpdateEntriesInOrder(IEnumerable<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry != null));
        _baseState = TerminalDisplayState.Ready;
        InvalidateEntries();
        ClampScroll();
    }

    /// <summary>Marks the view offline and clears stale entries.</summary>
    public void SetOffline()
    {
        _entries.Clear();
        _baseState = TerminalDisplayState.Offline;
        _scrollRow = 0;
        InvalidateEntries();
    }

    /// <summary>Updates the search query and scrolls back to the first row.</summary>
    public void SetSearchText(string? searchText)
    {
        var query = searchText ?? string.Empty;
        if (string.Equals(_searchText, query, StringComparison.Ordinal)) return;
        _searchText = query;
        _scrollRow = 0;
        _page = null;
    }

    /// <summary>Moves to an explicitly clamped first visible row.</summary>
    public void SetScroll(int row)
    {
        var clamped = Math.Max(0, Math.Min(row, GetMaxScroll()));
        if (clamped == _scrollRow) return;
        _scrollRow = clamped;
        _page = null;
    }

    /// <summary>Moves the first visible row by a clamped number of rows.</summary>
    public void ScrollBy(int rows) => SetScroll(_scrollRow + rows);

    /// <summary>Gets the number of rows the filtered results occupy.</summary>
    public int GetTotalRows() => (GetFilteredEntries(_searchText).Length + Columns - 1) / Columns;

    /// <summary>Gets <c>max(0, totalRows - visibleRows)</c>.</summary>
    public int GetMaxScroll() => Math.Max(0, GetTotalRows() - Rows);

    /// <summary>Gets the current view with fixed slot count and stable ordering.</summary>
    public StorageScreenPage GetPage()
    {
        var filtered = GetFilteredEntries(_searchText);
        if (_page != null) return _page;
        var maxScroll = Math.Max(0, (filtered.Length + Columns - 1) / Columns - Rows);
        _scrollRow = Math.Min(_scrollRow, maxScroll);
        var slotCount = Columns * Rows;
        var visible = filtered.Skip(_scrollRow * Columns).Take(slotCount).ToArray();
        var slots = new List<TerminalSlotView>(slotCount);
        for (var index = 0; index < slotCount; index++)
        {
            slots.Add(new TerminalSlotView(index, index < visible.Length ? visible[index] : null, CellPixels));
        }

        var state = _baseState == TerminalDisplayState.Ready && filtered.Length == 0
            ? TerminalDisplayState.Empty
            : _baseState;
        return _page = new StorageScreenPage(state, _searchText, _scrollRow, maxScroll, slots);
    }

    /// <summary>Shares the cached query result with input coloring, including render-before-input-callback order.</summary>
    public bool HasSearchResults(string? query) => GetFilteredEntries(query ?? string.Empty).Length > 0;

    private TerminalEntry[] GetFilteredEntries(string query)
    {
        if (!_filterDirty && _filterConfigurationRevision == _search.ConfigurationRevision &&
            string.Equals(_filterText, query, StringComparison.Ordinal)) return _filteredEntries;
        var predicate = _search.CreatePredicate(query);
        var filtered = _entries.Where(entry => predicate(entry.Document)).ToArray();
        _filteredEntries = filtered;
        _filterText = query;
        _filterConfigurationRevision = _search.ConfigurationRevision;
        _filterDirty = false;
        _page = null;
        return filtered;
    }

    private void InvalidateEntries()
    {
        _filterDirty = true;
        _filteredEntries = Array.Empty<TerminalEntry>();
        _page = null;
    }

    private static TerminalSearchService CreateDefaultSearch()
    {
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, configuration => configuration.Tags));
        registry.RegisterField(new SearchFieldProvider("types", document => document.ItemTypes, configuration => configuration.ItemTypes));
        registry.RegisterField(new SearchFieldProvider("descriptions", document => document.Descriptions, configuration => configuration.Descriptions));
        registry.RegisterField(new SearchFieldProvider("identifier", document => new[] { document.Identifier }, configuration => configuration.Identifiers));
        registry.RegisterPrefix('#', "tags"); registry.RegisterPrefix('@', "types");
        registry.RegisterPrefix('$', "descriptions"); registry.RegisterPrefix('&', "identifier");
        return new TerminalSearchService(registry, new SearchConfiguration());
    }

    private void ClampScroll()
    {
        var row = Math.Min(_scrollRow, GetMaxScroll());
        if (row != _scrollRow) _page = null;
        _scrollRow = row;
    }
}
