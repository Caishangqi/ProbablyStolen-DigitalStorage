using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;

/// <summary>
/// Groups the exact entries of the storage view into display groups. At most one projector may be active in a
/// terminal; the storage view uses the identity projection otherwise.
/// </summary>
public interface IStorageEntryProjector
{
    /// <summary>Gets the unique id.</summary>
    string Id { get; }

    /// <summary>Gets the derived attribute names the projector reads; the platform reads them only while it is active.</summary>
    IReadOnlyCollection<string> RequiredAttributes { get; }

    /// <summary>Whether the projector groups entries in this terminal, for example with its module installed.</summary>
    bool IsActive(TerminalContext context);

    /// <summary>Proposes groups; every entry must appear in exactly one group of its own item definition.</summary>
    IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries, TerminalContext context,
        EntryAttributes attributes);
}

/// <summary>Lays tinted overlay images over slots, the storage view counterpart of an item decorator.</summary>
public interface ISlotDecorator
{
    /// <summary>Gets the unique id.</summary>
    string Id { get; }

    /// <summary>Gets the order; overlays of a lower order are drawn first, below the others.</summary>
    int Order { get; }

    /// <summary>Whether the decorator runs in this terminal.</summary>
    bool IsActive(TerminalContext context);

    /// <summary>Adds the overlays of one slot.</summary>
    void Decorate(DisplayEntry entry, TerminalContext context, SlotDecorationSink sink);
}

/// <summary>
/// Adds lines to registered tooltip panels next to the native tooltip of a slot. The native tooltip itself is never
/// replaced.
/// </summary>
public interface IEntryTooltipContributor
{
    /// <summary>Gets the unique id.</summary>
    string Id { get; }

    /// <summary>Gets the order of the added lines.</summary>
    int Order { get; }

    /// <summary>Whether the contributor runs in this terminal.</summary>
    bool IsActive(TerminalContext context);

    /// <summary>Adds the lines of one slot.</summary>
    void Contribute(DisplayEntry entry, TerminalContext context, EntryTooltipSink sink);
}

/// <summary>Reads and writes small named values stored on the terminal item itself.</summary>
public interface ITerminalFeatureData
{
    /// <summary>Reads a value, or null when the terminal has none.</summary>
    string? Read(string key);

    /// <summary>Writes a value; false when the terminal can no longer be written, for example after a load.</summary>
    bool TryWrite(string key, string value);
}

/// <summary>Feature data of a window without a terminal item: nothing is stored.</summary>
public sealed class NoTerminalFeatureData : ITerminalFeatureData
{
    /// <summary>Gets the shared instance.</summary>
    public static NoTerminalFeatureData Instance { get; } = new();

    /// <inheritdoc />
    public string? Read(string key) => null;

    /// <inheritdoc />
    public bool TryWrite(string key, string value) => false;
}

/// <summary>Named numbers read from stored items for an active projector, detached from game objects.</summary>
public sealed class EntryAttributes
{
    private static readonly IReadOnlyDictionary<string, long> None = new Dictionary<string, long>();
    private readonly IReadOnlyDictionary<ItemKey, IReadOnlyDictionary<string, long>> _values;

    /// <summary>Creates attributes.</summary>
    public EntryAttributes(IReadOnlyDictionary<ItemKey, IReadOnlyDictionary<string, long>> values) =>
        _values = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>Gets attributes with no values.</summary>
    public static EntryAttributes Empty { get; } = new(new Dictionary<ItemKey, IReadOnlyDictionary<string, long>>());

    /// <summary>Gets the values read for one entry; empty when none were read.</summary>
    public IReadOnlyDictionary<string, long> For(ItemKey key) =>
        key != null && _values.TryGetValue(key, out var values) ? values : None;
}

/// <summary>Collects the overlays one slot receives, bottom first.</summary>
public sealed class SlotDecorationSink
{
    private readonly List<SlotDecoration> _items = new();

    internal IReadOnlyList<SlotDecoration> Items => _items;

    /// <summary>Adds an overlay above the ones added before.</summary>
    public void Add(string overlayKey, int tintRgb) => _items.Add(new SlotDecoration(overlayKey, tintRgb));
}

/// <summary>Collects the lines one tooltip receives.</summary>
public sealed class EntryTooltipSink
{
    private readonly List<TooltipLine> _lines = new();

    internal IReadOnlyList<TooltipLine> Lines => _lines;

    /// <summary>Adds a line to its panel.</summary>
    public void Append(TooltipLine line) => _lines.Add(line ?? throw new ArgumentNullException(nameof(line)));

    /// <summary>Adds a title line.</summary>
    public void Title(string panelId, string textKey, params object[] args) =>
        Append(new TooltipLine(panelId, TooltipRole.Title, textKey, args));

    /// <summary>Adds a caption line.</summary>
    public void Label(string panelId, string textKey, params object[] args) =>
        Append(new TooltipLine(panelId, TooltipRole.Label, textKey, args));

    /// <summary>Adds a content line.</summary>
    public void Body(string panelId, string textKey, params object[] args) =>
        Append(new TooltipLine(panelId, TooltipRole.Body, textKey, args));
}
