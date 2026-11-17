using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;

/// <summary>
/// Maps a store rejection text key to the short reason that ends a drag tooltip. The game's own refused-drag tooltips
/// are one line naming the item, the target and a brief cause, so the full rejection sentences do not fit there.
/// </summary>
public static class DropRefusalReasons
{
    /// <summary>The reason shown for a rejection without a short form of its own.</summary>
    public const string FallbackKey = "terminal_drag_reason_unsupported";

    private static readonly Dictionary<string, string> Reasons = new(StringComparer.Ordinal)
    {
        ["terminal_storage_reject_not_owned"] = "terminal_drag_reason_not_owned",
        ["terminal_storage_whole_stack_only"] = "terminal_drag_reason_whole_stack",
        ["terminal_storage_unsupported_item"] = FallbackKey,
        ["terminal_storage_unavailable"] = "terminal_drag_reason_offline",
        ["storage_restore_unavailable"] = "terminal_drag_reason_store_disabled",
        ["terminal_storage_reject_no_cell"] = "terminal_drag_reason_no_cell",
        ["terminal_storage_reject_type_limit"] = "terminal_drag_reason_type_limit",
        ["terminal_storage_reject_too_large"] = "terminal_drag_reason_too_large",
        ["terminal_storage_reject_no_space"] = "terminal_drag_reason_full",
        ["terminal_storage_reject_capacity"] = "terminal_drag_reason_capacity",
        ["terminal_storage_reject_quota"] = "terminal_drag_reason_quota",
        ["terminal_storage_reject_read_only"] = "terminal_drag_reason_read_only",
        ["terminal_storage_reject_partition_unknown"] = "terminal_drag_reason_partition_unknown",
        ["terminal_storage_reject_partition_invalid"] = "terminal_drag_reason_partition_invalid"
    };

    /// <summary>Gets every short reason key, for checks that each one has text.</summary>
    public static IReadOnlyCollection<string> AllReasonKeys =>
        new HashSet<string>(Reasons.Values, StringComparer.Ordinal) { FallbackKey };

    /// <summary>Gets the short reason key for a rejection key; unknown or missing keys get the fallback.</summary>
    public static string ReasonKey(string? rejectionKey) =>
        rejectionKey != null && Reasons.TryGetValue(rejectionKey, out var reason) ? reason : FallbackKey;
}
