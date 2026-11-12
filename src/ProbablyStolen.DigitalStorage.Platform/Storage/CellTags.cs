using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Reads and writes the storage tags kept on a cell item. Item tags are part of the native <c>itemState</c>,
/// so the native save graph persists them with the cell. Their meaning is decided by Common <see cref="CellRecord"/>.
/// Unknown or invalid values are never rewritten here; readers report them and the cell becomes unavailable.
/// </summary>
internal static class CellTags
{
    internal const string SchemaTag = "digital_storage.schema";
    internal const string LayoutTag = "digital_storage.layout";
    internal const string PageCountTag = "digital_storage.page_count";

    /// <summary>Gets the storage schema; a cell without the tag is schema 1.</summary>
    internal static int ReadSchema(GameItem cell) =>
        CellRecord.Schema(cell.IsTag(SchemaTag), cell.IsTag(SchemaTag) ? cell.GetTagReadonly(SchemaTag).GetInt() : 0);

    internal static void WriteSchema(GameItem cell, int schema) =>
        cell.ModifyTag(SchemaTag, state => state.Enable().SetInt(schema));

    /// <summary>
    /// Gets the page layout: the descriptor when present, otherwise the schema 1 page count. Values that cannot be
    /// read give false; they are never shortened, because native decoding restores child items by page position.
    /// </summary>
    internal static bool TryReadLayout(GameItem cell, out LayoutDescriptor? layout)
    {
        var text = cell.IsTag(LayoutTag) ? cell.GetTagReadonly(LayoutTag).GetString() ?? string.Empty : null;
        var hasCount = cell.IsTag(PageCountTag);
        return CellRecord.TryLayout(text, hasCount, hasCount ? cell.GetTagReadonly(PageCountTag).GetInt() : 0, out layout);
    }

    /// <summary>
    /// Records a new cell or housing with the current schema and only its anchor page.
    /// <c>SaveManager.DecodeNodesInternal</c> replaces the factory's entire item state with the saved state, so cells
    /// saved without these tags still load as schema 1.
    /// </summary>
    internal static void WriteNew(GameItem cell)
    {
        WriteLayout(cell, LayoutDescriptor.AnchorOnly());
        WriteSchema(cell, CellSchema.Current);
    }

    /// <summary>Writes the layout descriptor and the downgrade guard page count together.</summary>
    internal static void WriteLayout(GameItem cell, LayoutDescriptor layout)
    {
        var text = layout.Format();
        cell.ModifyTag(LayoutTag, state => state.Enable().SetString(text));
        cell.ModifyTag(PageCountTag, state => state.Enable().SetInt(CellRecord.DowngradeGuardPageCount));
    }

    /// <summary>Gets whether the recorded layout equals <paramref name="layout"/>.</summary>
    internal static bool HasLayout(GameItem cell, LayoutDescriptor layout) =>
        TryReadLayout(cell, out var recorded) && layout.Equals(recorded);
}
