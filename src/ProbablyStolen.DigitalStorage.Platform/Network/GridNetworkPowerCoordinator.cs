using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Platform.Energy;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Network;

internal sealed class GridNetworkPowerCoordinator
{
    private readonly NativeNetworkScanner _scanner;
    private readonly EnergyService _energyService = new();
    private readonly StorageService _storage;

    private readonly Action<string> _log;

    internal GridNetworkPowerCoordinator(NativeNetworkScanner scanner, Action<string> log, Func<GameItem, bool> migrate)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _storage = new StorageService(scanner, IsPowered, log, migrate);
    }

    internal TerminalInventorySnapshot ReadTerminalInventory(GameItem terminal) => _storage.GetCachedInventory(terminal);

    internal GameItem? FindStoredSample(GameItem terminal, ItemKey key) => _storage.FindSample(terminal, key);

    internal IReadOnlyList<CellBayReference> ListCellBays(GameItem terminal) => _storage.ListCellBays(terminal);

    internal long StackLimit(GameItem terminal, ItemKey key) => _storage.StackLimit(terminal, key);

    internal TerminalInsertionResult StoreSelected(GameItem terminal, IReadOnlyList<GameItem> items,
        Action<string>? reportDiagnostic = null) => _storage.Store(terminal, items);

    /// <summary>Stores one dragged root; part of a stack is not split off, so a partial drag is rejected.</summary>
    internal TerminalInsertionResult StoreSingle(GameItem terminal, GameItem item, int amount,
        Action<string>? reportDiagnostic = null)
    {
        if (amount >= item.unitCount)
        {
            return _storage.Store(terminal, new[] { item });
        }

        _log($"Digital Storage: store rejected item={item.identifier} units={item.unitCount} dragged={amount} reason=PartialStack");
        return TerminalInsertionResult.Create(TerminalInsertionStatus.Rejected, 0, "terminal_storage_whole_stack_only");
    }

    internal bool CanStore(GameItem terminal, GameItem item, int amount) =>
        amount >= item.unitCount && _storage.CanStore(terminal, item);

    internal TerminalExtractionResult ExtractToPlayer(GameItem terminal, ItemKey key, long amount) =>
        _storage.Extract(terminal, key, amount);

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
