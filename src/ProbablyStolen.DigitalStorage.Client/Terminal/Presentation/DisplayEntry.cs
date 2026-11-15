using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>
/// One storage slot: one or more exact entries of the same item definition shown together. The members are in the
/// order of the current sort, so the first one is the piece a click takes and the tooltip describes.
/// </summary>
public sealed class DisplayEntry
{
    /// <summary>Creates a display group.</summary>
    /// <param name="id">A stable identifier: equal groups under the same settings have equal identifiers.</param>
    /// <param name="members">The members in group order; at least one, all of one item definition.</param>
    public DisplayEntry(string id, IEnumerable<TerminalEntry> members)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("A group id is required.", nameof(id)) : id;
        if (members == null) throw new ArgumentNullException(nameof(members));
        var list = members.ToList();
        if (list.Count == 0 || list.Any(member => member == null))
            throw new ArgumentException("A group needs members.", nameof(members));
        if (list.Select(member => member.Key.Identifier).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new ArgumentException("A group holds one item definition.", nameof(members));
        Members = new ReadOnlyCollection<TerminalEntry>(list);
        long total = 0;
        long units = 0;
        foreach (var member in list)
        {
            total = checked(total + member.Amount);
            units = checked(units + member.TotalUnits);
        }

        Total = total;
        TotalUnits = units;
    }

    /// <summary>Gets the stable identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the members in group order.</summary>
    public IReadOnlyList<TerminalEntry> Members { get; }

    /// <summary>Gets the first member, which supplies the picture, name and tooltip.</summary>
    public TerminalEntry Representative => Members[0];

    /// <summary>Gets the stored pieces of all members.</summary>
    public long Total { get; }

    /// <summary>Gets the units of all members.</summary>
    public long TotalUnits { get; }

    /// <summary>Gets whether more than one exact entry is shown in this slot.</summary>
    public bool IsMixed => Members.Count > 1;

    /// <summary>Wraps one entry as its own group.</summary>
    public static DisplayEntry Single(TerminalEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        return new DisplayEntry(IdentityProjection.IdOf(entry), new[] { entry });
    }
}

/// <summary>A group a projector proposes: an identifier and its members in any order.</summary>
public sealed class ProjectedGroup
{
    /// <summary>Creates a proposed group.</summary>
    public ProjectedGroup(string id, IReadOnlyList<TerminalEntry> members)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("A group id is required.", nameof(id)) : id;
        Members = members ?? throw new ArgumentNullException(nameof(members));
    }

    /// <summary>Gets the identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the members.</summary>
    public IReadOnlyList<TerminalEntry> Members { get; }
}

/// <summary>The built-in projection used when no projector is active: every entry is its own group.</summary>
public static class IdentityProjection
{
    /// <summary>Gets the group identifier of a single entry.</summary>
    public static string IdOf(TerminalEntry entry) => "k:" + entry.GetIdentitySortKey();

    /// <summary>Groups each entry alone.</summary>
    public static IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        return entries.Select(entry => new ProjectedGroup(IdOf(entry), new[] { entry })).ToArray();
    }
}

/// <summary>
/// An image laid over a whole slot and tinted. The image itself decides where its mark sits; the quantity badge is
/// always drawn above it.
/// </summary>
public sealed class SlotDecoration
{
    /// <summary>Creates a decoration.</summary>
    /// <param name="overlayKey">The registered overlay image.</param>
    /// <param name="tintRgb">The tint as 0xRRGGBB; white keeps the image's own colors.</param>
    public SlotDecoration(string overlayKey, int tintRgb)
    {
        OverlayKey = string.IsNullOrEmpty(overlayKey)
            ? throw new ArgumentException("An overlay key is required.", nameof(overlayKey))
            : overlayKey;
        TintRgb = tintRgb is >= 0 and <= 0xFFFFFF ? tintRgb : throw new ArgumentOutOfRangeException(nameof(tintRgb));
    }

    /// <summary>Gets the overlay image key.</summary>
    public string OverlayKey { get; }

    /// <summary>Gets the tint as 0xRRGGBB.</summary>
    public int TintRgb { get; }
}

/// <summary>How a tooltip line is colored within its panel.</summary>
public enum TooltipRole
{
    /// <summary>The panel heading.</summary>
    Title,

    /// <summary>A caption above other lines.</summary>
    Label,

    /// <summary>Ordinary content.</summary>
    Body
}

/// <summary>One line an extension adds to a registered tooltip panel next to the native tooltip.</summary>
public sealed class TooltipLine
{
    /// <summary>Creates a line from a localization key and its arguments.</summary>
    public TooltipLine(string panelId, TooltipRole role, string textKey, params object[] args)
    {
        PanelId = string.IsNullOrEmpty(panelId) ? throw new ArgumentException("A panel id is required.", nameof(panelId)) : panelId;
        if (!Enum.IsDefined(typeof(TooltipRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
        Role = role;
        TextKey = string.IsNullOrEmpty(textKey) ? throw new ArgumentException("A text key is required.", nameof(textKey)) : textKey;
        Args = args ?? Array.Empty<object>();
    }

    /// <summary>Gets the panel the line belongs to.</summary>
    public string PanelId { get; }

    /// <summary>Gets the role that picks the line color.</summary>
    public TooltipRole Role { get; }

    /// <summary>Gets the localization key.</summary>
    public string TextKey { get; }

    /// <summary>Gets the format arguments.</summary>
    public IReadOnlyList<object> Args { get; }
}

/// <summary>A format argument the view localizes before formatting the line that holds it.</summary>
public sealed class LocalizedText
{
    /// <summary>Creates a localized argument.</summary>
    public LocalizedText(string textKey, params object[] args)
    {
        TextKey = string.IsNullOrEmpty(textKey) ? throw new ArgumentException("A text key is required.", nameof(textKey)) : textKey;
        Args = args ?? Array.Empty<object>();
    }

    /// <summary>Gets the localization key.</summary>
    public string TextKey { get; }

    /// <summary>Gets the format arguments; they may be localized texts too.</summary>
    public IReadOnlyList<object> Args { get; }
}
