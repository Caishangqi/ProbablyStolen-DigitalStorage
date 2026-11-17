using System;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Decides whether a root may enter a network at all, before any cell is asked. Every store path, terminal or API,
/// simulated or performed, goes through this rule.
/// </summary>
internal static class StoreAdmission
{
    internal const string ModItemPrefix = "digital_storage.";
    internal const string UnsupportedKey = "terminal_storage_unsupported_item";
    internal const string NotOwnedKey = "terminal_storage_reject_not_owned";

    /// <summary>Returns the rejection text key, or null when the root may be stored.</summary>
    internal static string? Refusal(GameItem item)
    {
        if (!IsStorable(item)) return UnsupportedKey;
        // The player's own inventories only take owned roots; goods still on a customer's counter are unowned until
        // bought, so a network that took them would skip the sale.
        return GeneralHelper.IsItemOwned(item) ? null : NotOwnedKey;
    }

    /// <summary>
    /// Rejects mod items and containers holding them: storing a drive or cell inside a cell would nest the
    /// network inside its own storage.
    /// </summary>
    private static bool IsStorable(GameItem item) =>
        !IsModItem(item) && !GraphUtils.FindAllChildrenType<GameItem>(item).Any(IsModItem);

    internal static bool IsModItem(GameItem item) =>
        item.identifier?.StartsWith(ModItemPrefix, StringComparison.Ordinal) == true;
}
