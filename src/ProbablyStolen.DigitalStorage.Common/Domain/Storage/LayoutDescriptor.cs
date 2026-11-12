using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Immutable page list of one cell, stored in the <c>digital_storage.layout</c> tag as
/// <c>A;S{w}x{h}x{n}*k;L*k</c>. New entry kinds may be appended; existing kinds never change meaning.
/// </summary>
/// <remarks>
/// <c>A</c> is the factory 12x12 page and must come first, exactly once. <c>L</c> is a schema 1 12x12 page that
/// only exists while migrating. <c>S{w}x{h}x{n}</c> is a strip page of <c>n</c> slots of <c>wxh</c>.
/// <c>*k</c> repeats an entry <c>k >= 2</c> times. The format version lives in <c>digital_storage.schema</c>, so the
/// text carries no version prefix.
/// </remarks>
public sealed class LayoutDescriptor : IEquatable<LayoutDescriptor>
{
    /// <summary>The largest page count a descriptor may record.</summary>
    public const int MaxPages = 32768;

    // The longest single entry with its separator is "S12x12x16;"; repeats only shorten the text.
    private const int MaxEntryLength = 10;

    /// <summary>The longest descriptor text accepted.</summary>
    public const int MaxTextLength = MaxPages * MaxEntryLength;

    private readonly PageGeometry[] _pages;

    private LayoutDescriptor(PageGeometry[] pages)
    {
        _pages = pages;
        Pages = Array.AsReadOnly(pages);
    }

    /// <summary>Gets the pages in native page order; page 0 is the anchor.</summary>
    public IReadOnlyList<PageGeometry> Pages { get; }

    /// <summary>Builds the descriptor of a new cell: only the empty anchor page.</summary>
    public static LayoutDescriptor AnchorOnly() => new(new[] { PageGeometry.Anchor });

    /// <summary>Builds the migration start descriptor of a schema 1 cell: the anchor followed by legacy pages.</summary>
    /// <param name="pageCount">The schema 1 page count, including the factory page.</param>
    /// <exception cref="ArgumentOutOfRangeException">The count is outside 1 to <see cref="MaxPages"/>.</exception>
    public static LayoutDescriptor Legacy(int pageCount)
    {
        if (pageCount < 1 || pageCount > MaxPages)
            throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, "The page count is outside the descriptor limits.");
        var pages = new PageGeometry[pageCount];
        pages[0] = PageGeometry.Anchor;
        for (var index = 1; index < pageCount; index++) pages[index] = PageGeometry.Legacy;
        return new LayoutDescriptor(pages);
    }

    /// <summary>Strictly parses descriptor text; any invalid input fails without truncation.</summary>
    public static bool TryParse(string? text, out LayoutDescriptor? descriptor)
    {
        descriptor = null;
        if (string.IsNullOrEmpty(text) || text!.Length > MaxTextLength) return false;

        var pages = new List<PageGeometry>();
        var entries = text.Split(';');
        for (var index = 0; index < entries.Length; index++)
        {
            if (!TryParseEntry(entries[index], out var page, out var repeat)) return false;
            if ((page.Kind == PageKind.Anchor) != (index == 0) || (page.Kind == PageKind.Anchor && repeat != 1)) return false;
            if (repeat > MaxPages - pages.Count) return false;
            for (var copy = 0; copy < repeat; copy++) pages.Add(page);
        }

        descriptor = new LayoutDescriptor(pages.ToArray());
        return true;
    }

    /// <summary>Formats the descriptor, collapsing runs of equal pages into <c>*k</c>.</summary>
    public string Format()
    {
        var builder = new StringBuilder();
        var index = 0;
        while (index < _pages.Length)
        {
            var run = 1;
            while (index + run < _pages.Length && _pages[index + run].Equals(_pages[index])) run++;
            if (builder.Length > 0) builder.Append(';');
            AppendEntry(builder, _pages[index]);
            if (run > 1) builder.Append('*').Append(run.ToString(CultureInfo.InvariantCulture));
            index += run;
        }

        return builder.ToString();
    }

    /// <summary>Gets a descriptor with one more page at the end.</summary>
    /// <exception cref="ArgumentException">The page is an anchor page.</exception>
    /// <exception cref="InvalidOperationException">The descriptor already records <see cref="MaxPages"/> pages.</exception>
    public LayoutDescriptor WithAppended(PageGeometry page)
    {
        if (page.Kind == PageKind.Anchor) throw new ArgumentException("Only page 0 may be the anchor.", nameof(page));
        if (_pages.Length >= MaxPages) throw new InvalidOperationException("The descriptor is at its page limit.");
        var pages = new PageGeometry[_pages.Length + 1];
        Array.Copy(_pages, pages, _pages.Length);
        pages[_pages.Length] = page;
        return new LayoutDescriptor(pages);
    }

    /// <summary>Gets a descriptor without the given pages; the order of the remaining pages is kept.</summary>
    /// <exception cref="ArgumentNullException">No index collection was supplied.</exception>
    /// <exception cref="ArgumentException">The indices include the anchor page.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the descriptor.</exception>
    public LayoutDescriptor Without(IReadOnlyCollection<int> pageIndices)
    {
        if (pageIndices == null) throw new ArgumentNullException(nameof(pageIndices));
        var removed = new HashSet<int>();
        foreach (var index in pageIndices)
        {
            if (index == 0) throw new ArgumentException("The anchor page cannot be removed.", nameof(pageIndices));
            if (index < 0 || index >= _pages.Length) throw new ArgumentOutOfRangeException(nameof(pageIndices), index, "The page is outside the descriptor.");
            removed.Add(index);
        }

        return new LayoutDescriptor(_pages.Where((_, index) => !removed.Contains(index)).ToArray());
    }

    /// <inheritdoc />
    public bool Equals(LayoutDescriptor? other) => other != null && _pages.SequenceEqual(other._pages);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LayoutDescriptor);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var page in _pages) hash.Add(page);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => Format();

    private static void AppendEntry(StringBuilder builder, PageGeometry page)
    {
        switch (page.Kind)
        {
            case PageKind.Anchor:
                builder.Append('A');
                break;
            case PageKind.Legacy:
                builder.Append('L');
                break;
            default:
                builder.Append('S')
                    .Append(page.SlotSize.Width.ToString(CultureInfo.InvariantCulture)).Append('x')
                    .Append(page.SlotSize.Height.ToString(CultureInfo.InvariantCulture)).Append('x')
                    .Append(page.SlotCount.ToString(CultureInfo.InvariantCulture));
                break;
        }
    }

    private static bool TryParseEntry(string entry, out PageGeometry page, out int repeat)
    {
        page = default;
        repeat = 1;
        var star = entry.IndexOf('*');
        var body = star < 0 ? entry : entry.Substring(0, star);
        if (star >= 0 && (!TryParseNumber(entry.Substring(star + 1), out repeat) || repeat < 2)) return false;

        switch (body)
        {
            case "A":
                page = PageGeometry.Anchor;
                return true;
            case "L":
                page = PageGeometry.Legacy;
                return true;
        }

        if (body.Length < 2 || body[0] != 'S') return false;
        var parts = body.Substring(1).Split('x');
        if (parts.Length != 3 || !TryParseNumber(parts[0], out var width) || !TryParseNumber(parts[1], out var height) ||
            !TryParseNumber(parts[2], out var slots))
            return false;

        var size = new BoundingSize(width, height);
        if (!StripLayout.Fits(size) || slots < 1 || slots > StripLayout.SlotsPerPage) return false;
        page = PageGeometry.Strip(size, slots);
        return true;
    }

    // Canonical decimal only: no sign, no leading zero, at most five digits (enough for MaxPages).
    private static bool TryParseNumber(string text, out int value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 5 || (text.Length > 1 && text[0] == '0')) return false;
        foreach (var character in text)
        {
            if (character < '0' || character > '9') return false;
            value = value * 10 + (character - '0');
        }

        return true;
    }
}
