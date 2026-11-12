using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Pure interpretation of the raw values in a cell's storage tags; Platform <c>CellTags</c> reads the tags and asks
/// this class what they mean, so the rules are testable without the game.
/// </summary>
public static class CellRecord
{
    /// <summary>
    /// The <c>page_count</c> written next to every layout descriptor. It is above the range older mod versions
    /// accept, so they report the cell unavailable instead of silently losing the pages they cannot rebuild.
    /// </summary>
    public const int DowngradeGuardPageCount = LayoutDescriptor.MaxPages + 1;

    /// <summary>The <c>partition_mode</c> value of a blacklist.</summary>
    public const string BlacklistMode = "blacklist";

    /// <summary>The <c>partition_mode</c> value of a whitelist.</summary>
    public const string WhitelistMode = "whitelist";

    /// <summary>
    /// The removed unrestricted mode reads as a whitelist and preserves its rules.
    /// </summary>
    public const string RemovedUnrestrictedMode = "none";

    /// <summary>Gets the storage schema: the tag value, or <see cref="CellSchema.Untagged"/> without the tag.</summary>
    public static int Schema(bool present, int value) => present ? value : CellSchema.Untagged;

    /// <summary>
    /// Gets the page layout. A descriptor wins when present, whatever <c>page_count</c> says (it holds the downgrade
    /// guard). Without a descriptor, the schema 1 page count builds an anchor followed by legacy pages.
    /// </summary>
    /// <param name="layoutText">The descriptor text, or null without the tag.</param>
    /// <param name="pageCountPresent">Whether <c>page_count</c> exists.</param>
    /// <param name="pageCount">The <c>page_count</c> value when present.</param>
    /// <param name="layout">The layout, or null when the recorded values are not valid.</param>
    public static bool TryLayout(string? layoutText, bool pageCountPresent, int pageCount, out LayoutDescriptor? layout)
    {
        if (layoutText != null) return LayoutDescriptor.TryParse(layoutText, out layout);

        layout = null;
        var count = pageCountPresent ? pageCount : 1;
        if (count < 1 || count > LayoutDescriptor.MaxPages) return false;
        layout = LayoutDescriptor.Legacy(count);
        return true;
    }

    /// <summary>Gets the partition mode; a missing tag is the default whitelist, an unknown value is null.</summary>
    public static PartitionMode? PartitionMode(string? text) => text switch
    {
        null => Partition.PartitionMode.Whitelist,
        RemovedUnrestrictedMode => Partition.PartitionMode.Whitelist,
        WhitelistMode => Partition.PartitionMode.Whitelist,
        BlacklistMode => Partition.PartitionMode.Blacklist,
        _ => null
    };

    /// <summary>Gets the tag text of a partition mode.</summary>
    public static string PartitionModeText(PartitionMode mode) => mode switch
    {
        Partition.PartitionMode.Whitelist => WhitelistMode,
        Partition.PartitionMode.Blacklist => BlacklistMode,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown partition mode.")
    };

    /// <summary>Gets the pages native decoding must append after the factory page, or null when it must not restore.</summary>
    /// <param name="schema">The recorded schema.</param>
    /// <param name="layout">The recorded layout, or null when it could not be read.</param>
    /// <param name="existingPages">The pages the decoded item already has.</param>
    public static IReadOnlyList<PageGeometry>? RestorePlan(int schema, LayoutDescriptor? layout, int existingPages)
    {
        // A newer schema may use page kinds this version cannot size; restoring a guess could misplace items.
        var gate = CellSchema.Classify(schema);
        if (gate is SchemaGate.TooNew or SchemaGate.Invalid || layout == null) return null;
        if (existingPages < 1 || existingPages > layout.Pages.Count) return null;
        return layout.Pages.Skip(existingPages).ToArray();
    }
}
