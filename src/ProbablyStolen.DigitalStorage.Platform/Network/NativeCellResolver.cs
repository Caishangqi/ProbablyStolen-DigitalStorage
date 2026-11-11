using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

/// <summary>Finds the assembled storage cells mounted in a drive's bays, in bay order.</summary>
internal static class NativeCellResolver
{
    // One instance per native cell, reused across scans. GridStorage rejects one instance mounted twice by reference:
    // a single shared instance made every network with two or more cells unreadable, and keying by cell keeps that
    // check meaningful if a scan ever lists the same cell twice.
    private static readonly ConditionalWeakTable<GameItem, MountedCell> Mounted = new();

    /// <summary>Gets grid-status cells for a drive; per-cell availability is checked by the storage operations.</summary>
    internal static IReadOnlyList<IStorageCell> Get(GameItem drive) =>
        GetCellItems(drive).Select(cell => (IStorageCell)Mounted.GetValue(cell, _ => new MountedCell())).ToArray();

    /// <summary>Gets the native cell items mounted in a drive, in bay order.</summary>
    internal static IReadOnlyList<GameItem> GetCellItems(GameItem drive) =>
        GetBays(drive).Select(bay => bay.childItem).Where(BasicCellHandler.IsCell).Select(cell => cell!).ToArray();

    /// <summary>Gets every cell bay slot of a drive, empty or not, in bay order.</summary>
    internal static IReadOnlyList<GameSlotInventory> GetBays(GameItem drive)
    {
        if (drive == null)
        {
            throw new ArgumentNullException(nameof(drive));
        }

        return GraphUtils.FindAllChildrenType<GameSlotInventory>(drive)
            .Where(slot => slot.identifier != null &&
                slot.identifier.StartsWith(CellContentWindow.BayPrefix, StringComparison.Ordinal))
            .ToArray();
    }

    // Grid status scans run for every terminal operation and tooltip and only ask whether a cell is present, so they
    // use this cheap adapter instead of reading every page. Storing and extracting go through
    // StorageService.GetInventory, which mounts real BasicCellInventory instances; these calls refuse without
    // touching any item.
    private sealed class MountedCell : IStorageCell
    {
        public CellState GetStatus() => CellState.Empty;

        public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

        public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source) =>
            new(0, TransferStep.Rejected(StoreRejection.ReadOnly), new StorageChangeSet());

        public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode,
            IActionSource source) => new(0, TransferStep.SourceEmpty, new StorageChangeSet());

        public void GetAvailableStacks(KeyCounter output)
        {
        }

        public string GetDescription() => "status-only mounted cell";
    }
}
