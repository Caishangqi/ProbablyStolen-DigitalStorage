using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

// These objects model only the in-memory shop context, battery balance and inventory calls used by the links.
internal sealed class EmporiumEntry
{
#nullable disable annotations
    internal static EmporiumEntry Instance { get; set; }
#nullable restore annotations
    internal readonly GameGridInventory invElement;
    internal EmporiumEntry(GameGridInventory inventory) => invElement = inventory;
    internal IEnumerable<GameItem> GetAllItems() => throw new InvalidOperationException("unexpected load audit");
#nullable disable annotations
    internal readonly GameGridInventory backInvinvElement = null;
#nullable restore annotations
    internal readonly GraphNodeStorage? frontInvinvElement = null;
    internal readonly GraphNodeStorage? docInvElement = null;
    internal readonly GraphNodeStorage? showcaseElement = null;
    internal readonly GraphNodeStorage? trashInvElement = null;
    internal readonly GraphNodeStorage? hiddenElement = null;
    internal readonly GraphNodeStorage? afterhourWindow = null;
    internal readonly GraphNodeStorage? vendingFountainElement = null;
    internal readonly GraphNodeStorage? vendingMachineElement = null;
}

internal sealed class MapUIManager
{
    internal static MapUIManager? Instance { get; set; }
    internal bool IsOutside { get; set; }
}

internal sealed class PlayerStore
{
    internal static PlayerStore? Instance { get; set; }
    internal static bool HasInstance => Instance != null;
    internal readonly object? storeService = null;
    internal int playerCash = 0;
}

internal sealed class StoreStation
{
    internal readonly object? storeEventManager = null;
}

internal static class MachineHelper
{
    internal static GameSlotInventory? GetBatterySlot(GameItem controller) => controller.BatterySlot;
    internal static GraphNodeStorage? GetBodyStorage(GameItem machine, int index) => null;
    internal static MachineryPanel Panel(string label, MachineryTabs tabs, string role)
    {
        var panel = new MachineryPanel();
        panel.SetContent(tabs);
        return panel;
    }

    internal static MachineryIntake WrapInIntake(GameGridInventory grid) => new() { content = grid };
}

internal static class PowerHelper
{
    internal const string POWER_SOURCE_ITEM_TAG = "test_power_source";
    internal const string POWER_SOURCE_ITEM_ENERGY = "test_energy";

    internal static bool DrawPowerSource(GameItem source, int units)
    {
        var energy = source.GetTagReadonly(POWER_SOURCE_ITEM_ENERGY).GetInt();
        if (energy < units) return false;
        source.ModifyTag(POWER_SOURCE_ITEM_ENERGY, tag => tag.SetInt(energy - units));
        source.AfterPowerDraw?.Invoke();
        return true;
    }

    internal static void UpdateEnergyCreditSprite(GameItem source) { }
}

internal static class CurrentConverterHelper
{
    internal const string CURRENT_CONVERTER_ON_BOOL = "test_converter_on";
    internal const string CURRENT_CONVERTER_RATE_FLOAT = "test_converter_rate";
    internal static bool IsCurrentConverter(GameItem source) => false;
    internal static bool isNightCycle => throw new InvalidOperationException("fixture has only ordinary batteries");
    internal static float GetRate(GameItem source) => throw new InvalidOperationException("fixture has only ordinary batteries");
    internal static int ToCredits(int units, float rate) => throw new InvalidOperationException("fixture has only ordinary batteries");
    internal static bool CanSupply(GameItem source, int amount) => throw new InvalidOperationException("fixture has only ordinary batteries");
}

internal static class HypertechBatteryHelper
{
    internal static bool IsHypertechBattery(GameItem source) => false;
}

internal sealed record GridShape(int minX, int minY, int globalWidth = 1, int globalHeight = 2);

internal sealed class GridShapeBuilder
{
    private GridShape _shape;

    internal GridShapeBuilder(GridShape shape) => _shape = shape;

    internal GridShapeBuilder SetPosition(int x, int y)
    {
        _shape = _shape with { minX = x, minY = y };
        return this;
    }

    internal GridShape Build() => _shape;
}

internal sealed class SlotMarker
{
    internal SlotMarker(GameInventory owner) => inventory = owner;
    internal readonly GameInventory inventory;
    internal readonly GameItem? targetItem = null;
    internal readonly GameItem? item = null;
    internal int numTransfer => 1;
    internal GridShape itemGridShape => new(0, 0);
    internal int TryAcceptOnce(int requested) => throw new InvalidOperationException("unexpected native drag acceptance");
}

namespace ProbablyStolen.DigitalStorage.Platform.Identity
{
    internal enum NativeItemIdentityFailure { None }
    internal enum NativeItemIdentityReadStatus { Success }

    internal static class NativeItemIdentityReader
    {
        internal static (NativeItemIdentityReadStatus Status, NativeItemIdentityFailure Failure, ItemKey? Identity,
            string? Rule, string? At) Read(GameItem item) =>
            (NativeItemIdentityReadStatus.Success, NativeItemIdentityFailure.None, new ItemKey(item.identifier), null, null);

        internal static (string Rule, string At)[] FindAnomalies(ItemKey key) => Array.Empty<(string, string)>();
    }
}

namespace ProbablyStolen.DigitalStorage.Platform.Storage
{
    internal static class CellPageRestore
    {
        internal static bool IsApplied() => true;
        internal static Exception? TakeLastFailure() => throw new InvalidOperationException("unexpected restore audit");
    }

    // The source-linked storage service also compiles its drag paths. These tests do not model drag placement.
    internal sealed class InventoryDropTarget
    {
        private static InvalidOperationException Unsupported() => new("unexpected native drag target");
        internal GameInventory Inventory => throw Unsupported();
        internal GameItemElement? HoveredItem => throw Unsupported();
        internal IReadOnlyList<GameInventory> ItemInventories => throw Unsupported();
        internal InventoryDropTarget? ForSinglePlacement(long requested) => throw Unsupported();
        internal int? CenterFor(GridShape shape) => throw Unsupported();
        internal void RecordPlacement(string phase, GridShape? shape = null, int? center = null, SlotMarker? slot = null,
            int requestedUnits = 0, int moved = 0) => throw Unsupported();
        internal static bool IsSupportedItemInventory(GameInventory inventory, IReadOnlyList<GameInventory> all) => throw Unsupported();
        internal static bool IsUsable(GameInventory inventory, GameItemElement? item) => throw Unsupported();
        internal static bool IsItemTargetUsable(GameItemElement item, IReadOnlyList<GameInventory> all) => throw Unsupported();
    }
}
