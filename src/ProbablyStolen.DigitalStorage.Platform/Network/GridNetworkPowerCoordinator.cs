using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Platform.Energy;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

internal sealed class GridNetworkPowerCoordinator
{
    private const string WholeStackOnlyKey = "terminal_storage_whole_stack_only";
    private readonly NativeNetworkScanner _scanner;
    private readonly EnergyService _energyService = new();
    private readonly StorageService _storage;
    private readonly Action<string> _nativeLog;

    internal GridNetworkPowerCoordinator(NativeNetworkScanner scanner, Action<string> nativeLog, Func<GameItem, bool> migrate,
        CellDiagnostics diagnostics)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _nativeLog = nativeLog;
        _storage = new StorageService(scanner, IsPowered, nativeLog, migrate, diagnostics);
    }

    /// <summary>Gets the storage operations shared by terminals and the public API.</summary>
    internal StorageService Storage => _storage;

    internal TerminalInventorySnapshot ReadTerminalInventory(GameItem terminal) => _storage.GetCachedInventory(terminal);

    internal GameItem? FindStoredSample(GameItem terminal, ItemKey key) => _storage.FindSample(terminal, key);

    internal IReadOnlyList<CellBayReference> ListCellBays(GameItem terminal) => _storage.ListCellBays(terminal);

    internal TerminalInsertionResult StoreSelected(GameItem terminal, IReadOnlyList<GameItem> items,
        TransferOperation operation) => _storage.Store(terminal, items, operation);

    /// <summary>Stores one dragged root; part of a stack is not split off, so a partial drag is rejected.</summary>
    internal TerminalInsertionResult StoreSingle(GameItem terminal, GameItem item, int amount,
        TransferOperation? operation = null)
    {
        if (amount >= item.unitCount)
        {
            return _storage.Store(terminal, new[] { item }, operation);
        }

        if (operation == null)
            _nativeLog($"Digital Storage: store rejected item={item.identifier} units={item.unitCount} dragged={amount} reason=PartialStack");
        return TerminalInsertionResult.Create(TerminalInsertionStatus.Rejected, 0, WholeStackOnlyKey);
    }

    /// <summary>
    /// Gets why each dragged item could not be stored as a text key, or null when it could, reading the network once.
    /// A single drag passes its dragged <paramref name="amount"/>; a group drag passes null, as it moves whole stacks.
    /// </summary>
    internal string?[] StoreRefusals(GameItem terminal, IReadOnlyList<GameItem> items, int? amount)
    {
        var refusals = new string?[items.Count];
        var whole = new List<GameItem>(items.Count);
        var positions = new List<int>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            if ((amount ?? items[index].unitCount) < items[index].unitCount)
            {
                refusals[index] = WholeStackOnlyKey;
                continue;
            }

            whole.Add(items[index]);
            positions.Add(index);
        }

        var stored = whole.Count == 0 ? Array.Empty<string?>() : _storage.StoreRefusals(terminal, whole);
        for (var index = 0; index < stored.Length; index++) refusals[positions[index]] = stored[index];
        return refusals;
    }

    internal TerminalExtractionResult ExtractPiecesToPlayer(GameItem terminal, ItemKey key, long pieces, TransferOperation operation) =>
        _storage.ExtractPieces(terminal, key, pieces, operation);

    internal TerminalExtractionResult ExtractBatch(GameItem terminal, TerminalDragBatch batch,
        InventoryDropTarget target, TransferOperation operation) => _storage.ExtractBatchTo(terminal, batch, target, operation);

    internal GridPowerResult OnControllerCycleEnd(GameItem controller)
    {
        var snapshot = _scanner.Capture(controller);
        var grid = snapshot.FindGrid(controller);
        if (grid.GetPathingService().GetControllerState() != ControllerState.Online)
        {
            return _energyService.OnTurnEnd(grid, null);
        }

        var source = new NativeEnergySource(snapshot.FindController(grid));
        return _energyService.OnTurnEnd(grid, source);
    }

    internal bool IsPowered(NativeGridSnapshot snapshot, Grid grid)
    {
        if (grid.GetPathingService().GetControllerState() != ControllerState.Online)
        {
            return false;
        }

        return _energyService.IsNetworkPowered(grid,
            new NativeEnergySource(snapshot.FindController(grid)));
    }
}
