using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Events;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

internal sealed class IoPortTransferFixture : IDisposable
{
    private readonly NativeNetworkScanner _scanner;
    private readonly IoPortCoordinator _coordinator;
    private readonly StorageEventBus _events;
    private readonly StorageService _storage;
    private readonly CellDiagnostics _cellDiagnostics;
    private int _frame;

    internal IoPortTransferFixture(IoPortDirection direction = IoPortDirection.Forward, bool includeDrive = true,
        IoPortCompletionMode completion = IoPortCompletionMode.WorkComplete, TimeSpan? frameBudget = null)
    {
        var categories = new LogCategories();
        var transferCategory = categories.Register("log_transfer", null, "transfer", LogVerbosity.Warning, true);
        var cellCategory = categories.Register("log_cell", null, "cell", LogVerbosity.Warning, true);
        categories.Freeze();
        var logger = new DiagnosticLogger(categories,
            _ => new LogCategorySettings(true, LogVerbosity.Warning), (_, line) => Logs.Add(line));
        var transferDiagnostics = new TransferDiagnostics(logger, transferCategory);
        _cellDiagnostics = new CellDiagnostics(logger, cellCategory);
        _events = new StorageEventBus(error => Assert.Fail(error));
        _scanner = new NativeNetworkScanner(new PowerSettings(), storageCellResolver: NativeCellResolver.Get);
        _storage = new StorageService(_scanner, (_, _) => true, _ => { }, _ => true, _cellDiagnostics);

        Grid = new GameGridInventory();
        var entry = new EmporiumEntry(Grid);
        Controller = new GameItem(ControllerItem.ItemId) { uniqueId = 10 };
        Machine = NewMachine(11, new IoPortSettings(direction, completion, running: true));
        Grid.Place(Controller);
        Grid.Place(Machine);
        Grid.Touch(Controller, Machine);
        if (includeDrive)
        {
            var drive = new GameItem(DriveItem.ItemId) { uniqueId = 12 };
            Grid.Place(drive);
            Grid.Touch(Machine, drive);
            var driveBay = Bay(CellContentWindow.BayPrefix + "0", drive);
            drive.Slots.Add(driveBay);
            DriveCell = Cell(103);
            driveBay.Place(DriveCell);
        }
        (Left, Right) = CreateBanks(Machine);
        Left[0].Place(Cell(101));
        var batterySlot = new GameSlotInventory { identifier = "battery", Owner = Controller };
        Controller.BatterySlot = batterySlot;
        var battery = new GameItem("ordinary_battery") { Owned = true };
        battery.state = PowerSourceState(100);
        battery.modifiedState = battery.state.Clone();
        battery.AfterPowerDraw = () =>
        {
            FeeDraws++;
            AfterPowerDraw?.Invoke();
        };
        batterySlot.Place(battery);
        Controller.Slots.Add(batterySlot);

        // Each callback runs in its own frame with a generous budget, so the time slice never decides a test.
        _coordinator = new IoPortCoordinator(_scanner, _storage, transferDiagnostics, new NativeNetworkEvents(_events),
            Tuning, () => _frame, frameBudget ?? TimeSpan.FromSeconds(10));
        _coordinator.Attach(Machine, Left, Right);
        PlayerStore.Instance = new PlayerStore();
        EmporiumEntry.Instance = entry;
        _coordinator.StartRun(PlayerStore.Instance, new StoreStation());
        Assert.That(IoPortNativeState.TryRead(Machine, out var state), Is.True);
        Assert.That(state!.Settings.Direction, Is.EqualTo(direction));
        Assert.That(_scanner.Capture(Machine).FindGrid(Machine).GetPathingService().GetControllerState(),
            Is.EqualTo(ControllerState.Online));
    }

    internal List<string> Logs { get; } = new();
    internal MutableTuning Tuning { get; } = new();

    internal sealed class MutableTuning : IIoPortTuning
    {
        public int RootsPerSecond { get; set; } = 64;
        public int RootsPerFee { get; set; } = 100;
    }
    internal GameGridInventory Grid { get; }
    internal GameItem Controller { get; }
    internal GameItem Machine { get; }
    internal GameSlotInventory[] Left { get; }
    internal GameSlotInventory[] Right { get; }
    internal GameItem? DriveCell { get; }
    internal int DriveCount => Capture().FindGrid(Machine).DriveCount;
    internal NativeGridSnapshot Capture() => _scanner.Capture(Machine);
    internal int Energy => Controller.BatterySlot!.childItem!.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt();
    internal int FeeDraws { get; private set; }
    internal Action? AfterPowerDraw { get; set; }

    internal GameItem AddTerminal()
    {
        var terminal = new GameItem(TerminalItem.ItemId) { uniqueId = 13 };
        Grid.Place(terminal);
        Grid.Touch(Controller, terminal);
        return terminal;
    }

    internal GameSlotInventory AddDriveNamedBay(GameItem machine)
    {
        var bay = Bay(CellContentWindow.BayPrefix + "0", machine);
        machine.Slots.Add(bay);
        return bay;
    }

    internal TerminalInsertionResult Store(GameItem terminal, GameItem item) => _storage.Store(terminal, new[] { item }, operation: null);

    internal IoPortCoordinator Coordinator => _coordinator;

    /// <summary>Runs one game second in a new frame and returns what the window would show.</summary>
    internal IoPortDisplaySnapshot Callback(GameItem? machine = null)
    {
        SecondWithoutFrames(machine);
        // The rest of the second's quota is spent on the following frames.
        for (var frames = 0; frames < 1000 && _coordinator.HasOpenSession(machine ?? Machine); frames++) Frame();
        return _coordinator.ReadDisplay(machine ?? Machine);
    }

    /// <summary>Runs only the game-second callback in a new frame.</summary>
    internal IoPortDisplaySnapshot SecondWithoutFrames(GameItem? machine = null)
    {
        _frame++;
        _coordinator.OnGameSecond(machine ?? Machine);
        return _coordinator.ReadDisplay(machine ?? Machine);
    }

    /// <summary>Advances one frame.</summary>
    internal void Frame()
    {
        _frame++;
        _coordinator.OnFrame();
    }

    /// <summary>Runs game seconds until the machine is due again after a retry wait.</summary>
    internal IoPortDisplaySnapshot CallbackAfterRetry()
    {
        for (var second = 0; second < 5; second++) Callback();
        return Callback();
    }

    /// <summary>Runs one more game second in the current frame, as the compressed night simulation does.</summary>
    internal IoPortDisplaySnapshot CallbackInSameFrame()
    {
        _coordinator.OnGameSecond(Machine);
        return _coordinator.ReadDisplay(Machine);
    }

    internal IoPortNativeState State =>
        IoPortNativeState.TryRead(Machine, out var state) ? state! : throw new InvalidOperationException("unreadable state");
    internal GameGridInventory[] Pages(GameItem cell) => CellInventoryFixture.Tabs(cell).Inventories.ToArray();
    internal GameItem[] Roots(GameItem cell) => Pages(cell).SelectMany(page => page.childItems).ToArray();

    internal CellView ReadCell(GameItem cell) => BasicCellHandler.GetCellInventory(cell, _cellDiagnostics,
        useCachedContents: false)?.View ?? throw new InvalidOperationException("Expected an assembled and readable fixture cell.");

    internal GameItem AddCellToSlot(GameSlotInventory slot, int id)
    {
        Assert.That(slot.childItem, Is.Null);
        var cell = Cell(id);
        slot.Place(cell);
        Assert.That(slot.childItem, Is.SameAs(cell));
        Assert.That(cell.parentInventory, Is.SameAs(slot));
        return cell;
    }

    internal GameItem AddRoot(GameItem cell, int id)
    {
        // A transfer compacts the cells it emptied, which drops their empty strip page.
        if (Pages(cell).Length < 2)
            Assert.That(CellPages.TryAppendStripPage(cell, PageGeometry.Strip(new BoundingSize(1, 2), 16)), Is.True);
        var root = new GameItem("test.material")
        {
            uniqueId = id,
            shape = new GameItemShape { width = 1, height = 2 },
            modifiedShape = new GridShape(Pages(cell)[1].childItems.Count, 0)
        };
        Pages(cell)[1].Place(root);
        return root;
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _events.Dispose();
        foreach (var cell in Grid.childItems.SelectMany(item => item.Slots).Select(slot => slot.childItem)
                     .Where(item => item != null).Cast<GameItem>()) CellInventoryCache.Forget(cell);
        EmporiumEntry.Instance = null;
        PlayerStore.Instance = null;
    }

    private static GameItem NewMachine(int id, IoPortSettings settings)
    {
        var machine = new GameItem(IoPortItem.ItemId) { uniqueId = id, onOneSecondItemFunc = (_, _) => { } };
        var state = IoPortNativeState.Default.WithSettings(settings);
        if (!IoPortNativeState.TryWrite(machine, IoPortNativeState.Default, state))
            throw new InvalidOperationException("The fixture could not configure the IO Port.");
        return machine;
    }

    private static (GameSlotInventory[] Left, GameSlotInventory[] Right) CreateBanks(GameItem machine)
    {
        var left = new GameSlotInventory[IoPortSettings.SlotsPerBank];
        var right = new GameSlotInventory[IoPortSettings.SlotsPerBank];
        for (var index = 0; index < left.Length; index++)
        {
            left[index] = Bay("digital_storage.io_port_bay." + index, machine);
            right[index] = Bay("digital_storage.io_port_bay." + (index + left.Length), machine);
        }
        machine.Slots.AddRange(left);
        machine.Slots.AddRange(right);
        return (left, right);
    }

    private static GameSlotInventory Bay(string id, GameItem owner) => new() { identifier = id, Owner = owner };

    private static GameItem Cell(int id)
    {
        var cell = CellInventoryFixture.Create(id);
        cell.RemovalProbe = () => true;
        Assert.That(CellPages.TryAppendStripPage(cell, PageGeometry.Strip(new BoundingSize(1, 2), 16)), Is.True);
        return cell;
    }

    private static TagSystem PowerSourceState(int energy)
    {
        var tags = new TagSystem();
        tags.GetTag(PowerHelper.POWER_SOURCE_ITEM_ENERGY).Enable().SetInt(energy);
        tags.GetTag(PowerHelper.POWER_SOURCE_ITEM_TAG).Enable();
        return tags;
    }

    private sealed class PowerSettings : IGridPowerSettingsPort
    {
        public int GetPowerUsage(GridNodeKind kind) => kind == GridNodeKind.Controller ? 1 : 0;
    }
}
