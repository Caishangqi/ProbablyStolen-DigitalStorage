using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Partition window source for the cell mounted in one drive bay. It binds to the cell present when the bay is
/// selected; applying writes the partition tag on that cell and never moves stored items.
/// </summary>
internal sealed class CellPartitionSource : IPartitionWindowSource
{
    private readonly GameSlotInventory _bay;
    private readonly GameItem? _cell;

    internal CellPartitionSource(GameSlotInventory bay)
    {
        _bay = bay;
        var cell = bay.childItem;
        _cell = cell != null && CellPages.Tabs(cell) != null ? cell : null;
    }

    // Locked and unavailable cells keep their tags exactly as loaded.
    public bool IsCurrent => _cell != null && ReferenceEquals(_bay.childItem, _cell) && !SaveManager.IsDecoding &&
        CellHealth.Get(_cell).Status is not (CellHealthStatus.Locked or CellHealthStatus.Unavailable);

    public bool TryRead(out CellPartition? partition)
    {
        partition = IsCurrent ? CellTags.ReadPartition(_cell!) : null;
        // The window edits whitelists only; opening it on a blacklist would overwrite it as a whitelist.
        return partition?.IsValid == true && partition.Mode == IncludeExclude.Whitelist;
    }

    public bool TryReadContents(out IReadOnlyList<ItemKey>? keys)
    {
        keys = IsCurrent
            // Fallback keys of unreadable items are session-only, so they cannot be written into a partition.
            ? CellPages.Roots(_cell!).Select(StorageService.Identify).OfType<ItemKey>()
                .Where(key => key.IsCanonical).Distinct().ToArray()
            : null;
        return keys != null;
    }

    public bool TrySubmit(CellPartition candidate)
    {
        if (!IsCurrent || candidate == null || !candidate.IsValid)
        {
            return false;
        }

        var current = CellTags.ReadPartition(_cell!);
        if (current.IsValid && current.Mode != IncludeExclude.Whitelist) return false;
        // Keep the stored equal distribution flag; the window does not edit it.
        var limit = CellTypeLimitSource.EffectiveOrBase(_cell!);
        var merged = CellPartition.FromConfiguration(IncludeExclude.Whitelist, candidate.List.GetItems(),
            current.EqualDistribution, limit);
        if (!merged.IsValid) return false;
        CellTags.WritePartition(_cell!, merged);
        return true;
    }

    public void Dispose()
    {
    }
}
