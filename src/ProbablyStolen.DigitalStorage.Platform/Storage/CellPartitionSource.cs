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

    public bool IsCurrent => _cell != null && ReferenceEquals(_bay.childItem, _cell) && !SaveManager.IsDecoding;

    public bool TryRead(out CellPartition? partition)
    {
        partition = IsCurrent ? CellTags.ReadPartition(_cell!) : null;
        return partition?.IsValid == true;
    }

    public bool TryReadContents(out IReadOnlyList<ItemKey>? keys)
    {
        keys = IsCurrent
            // Fallback keys of unreadable items are session-only, so they cannot be written into a partition.
            ? CellPages.Roots(_cell!).Select(NetworkStorage.Identify).OfType<ItemKey>()
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

        CellTags.WritePartition(_cell!, candidate);
        return true;
    }

    public void Dispose()
    {
    }
}
