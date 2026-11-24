using System;
using System.Collections.Generic;
using System.Linq;

// Physical boundary doubles for the linked production scanner. They do not simulate native shape or Unity behavior.
internal sealed class GameItemShape
{
    internal int width;
    internal int height;
    internal int globalWidth => width;
    internal int globalHeight => height;
}

internal static class ModHook
{
    private static Action<RichTextBuilder, GameItem>? _tooltip;
    internal static bool ThrowAfterTooltipSubscription { get; set; }
    internal static int TooltipSubscriberCount => _tooltip?.GetInvocationList().Length ?? 0;
    internal static Action<RichTextBuilder, GameItem>? TooltipCallback => _tooltip;
    internal static event Action<RichTextBuilder, GameItem> OnCreateTooltipLate
    {
        add
        {
            _tooltip += value;
            if (ThrowAfterTooltipSubscription) throw new InvalidOperationException("injected hook subscription failure");
        }
        remove { _tooltip -= value; }
    }
    internal static void Reset() { _tooltip = null; ThrowAfterTooltipSubscription = false; }
    internal static void RaiseTooltip(RichTextBuilder builder, GameItem item) => _tooltip?.Invoke(builder, item);
}

internal static class ModuleHelper
{
    internal const string MODULE_STUCK_TAG = "MODULE_STUCK_BOOL";
    internal const string ADDITIONAL_EFFECT_STRING = "ADDITIONAL_EFFECT_STRING";

    public static void CreateModuleTooltip(RichTextBuilder builder, GameItem item)
    {
        if (item.IsTag(ADDITIONAL_EFFECT_STRING))
            builder.AddLine(item.GetTagReadonly(ADDITIONAL_EFFECT_STRING).GetString(), true, RenderHandler.ColorPalette.LightCyan);
    }

    internal static void InitModuleItem(GameItem item, string moduleType, bool isShipModule) =>
        throw new InvalidOperationException("unexpected item initialization");
}

internal static class SaveManager
{
    internal static bool IsDecoding { get; set; }
}

public sealed class TagState
{
    public int valueInt;
    public string valueString = string.Empty;
    public long valueLong = 0;
    public float valueFloat = 0;
    public double valueDouble = 0;
    public bool valueBool = false;
    public string identifier = string.Empty;
    public string identifierName = string.Empty;
    public bool valueEnabled;
    public bool GetBool() => valueEnabled;
    public bool IsEnabled() => valueEnabled;
    public int GetInt() => valueInt;
    public string GetString() => valueString;
    public TagState Clone() => (TagState)MemberwiseClone();
    public TagState SetString(string value) { valueString = value; return this; }
    public TagState Enable() { valueEnabled = true; return this; }
    public TagState SetInt(int value) { valueInt = value; return this; }
}

public sealed class TagSystem
{
    public Dictionary<string, TagState> dict = new();
    public TagSystem Clone() => new() { dict = new Dictionary<string, TagState>(dict) };
    public void DisableTag(string key) => dict.Remove(key);
    public TagState GetTag(string key)
    {
        if (!dict.TryGetValue(key, out var state)) dict[key] = state = new TagState();
        return state;
    }
}

internal static class MachineryHelper
{
    internal const string MACHINERY_DRAWN_POWER = "machinery_drawn_power";
    internal const string MACHINERY_DRAWN_POWER_BASE = "MACHINERY_DRAWN_POWER_BASE";

    public static void CreateMachineryTooltip(RichTextBuilder builder, GameItem item)
    {
        if (item.GetTagReadonly(MACHINERY_DRAWN_POWER).GetInt() > 0)
            builder.AddLine("native_power", true, RenderHandler.ColorPalette.LightCyan);
    }
}

public class GameItem
{
    internal GameItem(string id)
    {
        identifier = id;
        state = new TagSystem();
        Stuck = false;
    }

    internal string identifier;
    public string shortDescription { get; set; } = string.Empty;
    public string flavorText { get; set; } = string.Empty;
    internal TagSystem state;
    internal TagSystem modifiedState = new();
    // The game assembly does not annotate these reference types.
#nullable disable annotations
    internal GameInventory parentInventory;
#nullable restore annotations
    internal PixelWindow? contentWindow;
    internal readonly List<GameSlotInventory> Slots = new();
    internal int unitCount = 1;
    internal GameItemShape shape = new() { width = 1, height = 2 };
    internal bool Stuck = false;
    internal bool ReceivingModuleBonus = false;
    internal bool ContainerTooltip = false;
    internal bool Owned = true;
    internal Func<bool>? RemovalProbe;
    internal Func<GameItem, GameItem, bool>? mayStackItemFunc;
    internal Func<GameItem, GameItem, int, int>? maxSlotAmountItemFunc;
    internal int TagWrites;
    internal int uniqueId = 0;
    internal int GetUniqueID() => uniqueId;
    internal GridShape modifiedShape = new(0, 0);
    internal GameSlotInventory? BatterySlot = null;
    internal Action? AfterPowerDraw = null;
    internal Action<GameItem, GameInventory>? onOneSecondItemFunc = null;
    internal Action<GameItem, GameInventory, object?>? onSlotAddItemFunc;
    internal Action<GameItem, GameInventory, object?>? onSlotRemoveItemFunc;
    internal string name = string.Empty;
    internal string identifierName = string.Empty;
    internal string spriteAtlasPath = string.Empty;
    internal string spritePath = string.Empty;
    internal string longDescription = string.Empty;
    internal IReadOnlyList<string> itemTypes = Array.Empty<string>();
    internal string GetDisplayName(bool preferCustomName) => identifier;
    internal void Destroy() => throw new InvalidOperationException("unexpected item destruction");
    internal void ModifyTag(string key, Action<TagState> change, bool skipValidate = false)
    {
        TagWrites++;
        change(state.GetTag(key));
        modifiedState = state.Clone();
    }
    internal void DisableTag(string tag)
    {
        state.DisableTag(tag);
        modifiedState = state.Clone();
    }

    internal void ValidateShapeState() => throw new InvalidOperationException("unexpected native shape validation");
    public bool MayRemove() => RemovalProbe?.Invoke() ?? throw new InvalidOperationException("unexpected removal probe");
    public int MaxNumRemove() => MayRemove() ? unitCount : 0;
    internal readonly List<GameItem> Children = new();

    internal bool IsTag(string tag) => tag == "CONTAINER_TAG" || tag == "CONTAINER_TOOLTIP_TAG"
        ? ContainerTooltip
        : (Stuck && tag == ModuleHelper.MODULE_STUCK_TAG) ||
          (modifiedState.dict.TryGetValue(tag, out var value) && value.valueEnabled);
    internal TagState GetTagReadonly(string tag) => ReceivingModuleBonus && tag == "IS_RECEIVING_BONUS_FROM_MODULE_BOOL"
        ? new TagState().Enable()
        : modifiedState.GetTag(tag).Clone();

    internal List<T> FindAdjacentType<T>() => parentInventory is GameGridInventory grid
        ? grid.Neighbors(this).OfType<T>().ToList()
        : new List<T>();
}

/// <summary>Stands in for the native graph node base type.</summary>
public class GraphNodeStorage
{
}

internal sealed class InventoryRoots : List<GameItem>
{
    internal int SnapshotReads { get; private set; }

    public new GameItem[] ToArray()
    {
        SnapshotReads++;
        return base.ToArray();
    }
}

public class GameInventory : GraphNodeStorage
{
    internal string identifier = string.Empty;
    internal GameItem? Owner;
    internal GraphNodeStorage? parent;
    internal readonly InventoryRoots childItems = new();
    internal readonly List<object> parents = new();
#nullable disable annotations
    internal GameItem GetParentItem() => Owner ?? (parent switch
    {
        MachineryPanelRow row => (row.parent as PixelWindow)?.parentItems.FirstOrDefault(),
        PixelWindow window => window.parentItems.FirstOrDefault(),
        _ => null
    });
#nullable restore annotations

    internal Action<GameItem, GameInventory, object?>? onSlotAddItemEndFunc;
    internal Action<GameItem, GameInventory, object?>? onSlotRemoveItemEndFunc;
    internal Action<GameItem, GameInventory, object?>? onSlotAddItemFunc = null;
    internal Action<GameItem>? AfterExpel = null;
    internal Action<GameItem>? AfterAccept = null;
    internal bool IsRemoveLocked() => false;
    internal bool IsInsertLocked() => false;

    internal void Place(GameItem item)
    {
        if (item.parentInventory is { } previous) previous.Remove(item);
        item.parentInventory = this;
        childItems.Add(item);
        if (this is GameSlotInventory slot && slot.childItem == null) slot.childItem = item;
    }

    internal bool Expel(GameItem item)
    {
        if (!ReferenceEquals(item.parentInventory, this) || !childItems.Contains(item)) return false;
        Remove(item);
        AfterExpel?.Invoke(item);
        return true;
    }

    internal void UncheckedAccept(GameItem item)
    {
        if (item.parentInventory != null || this is GameSlotInventory { childItem: not null })
            throw new InvalidOperationException("test inventory requires a detached item and a free slot");
        Place(item);
        AfterAccept?.Invoke(item);
    }

    internal SlotMarker? TryInventorySlot(GameItem item, int count, GridShape shape, TagSystem tags) =>
        throw new InvalidOperationException("unexpected native inventory search");

    internal SlotMarker? TryInventorySlot(GameItem item, int count, int center, GridShape shape, TagSystem tags) =>
        throw new InvalidOperationException("unexpected native point placement");

    private void Remove(GameItem item)
    {
        childItems.Remove(item);
        if (this is GameSlotInventory slot && ReferenceEquals(slot.childItem, item)) slot.childItem = null;
        item.parentInventory = null;
    }
}

public sealed class GameGridInventory : GameInventory
{
    public GameGridInventory() { }

    internal GameGridInventory(int width, int height) => inventoryShape = new GameItemShape { width = width, height = height };

    internal GameGridInventory SetMachineryStyle() => this;
    internal bool overrideLockInsert;
    internal GameItemShape inventoryShape = new();
    internal Func<GameItem, GameInventory, bool>? mayInventoryRemoveItemFunc;
    internal Func<GameItem, GameInventory, object?, int>? maxSlotRemoveItemFunc;
    internal Func<GameItem, GameInventory, object?, bool>? mayActivateSlotItemFunc;
    internal Func<GameItem, GameInventory, object?, bool>? canActivateSlotItemFunc;
    internal Func<GameItem, bool>? PlacementProbe = null;
    internal bool CheckShapePlacement(GameItem item, GridShape shape, List<GameItem> ignored) =>
        PlacementProbe?.Invoke(item) != false && childItems.All(root => ReferenceEquals(root, item) ||
            root.modifiedShape.minX != shape.minX || root.modifiedShape.minY != shape.minY);
    internal SlotMarker? TryFindOneValidInventorySlot(GameItem item, bool keepOrientation) =>
        throw new InvalidOperationException("unexpected native grid search");
    private readonly List<(GameItem First, GameItem Second)> _contacts = new();

    internal void Touch(GameItem first, GameItem second) => _contacts.Add((first, second));

    internal void Separate(GameItem item) =>
        _contacts.RemoveAll(contact => ReferenceEquals(contact.First, item) || ReferenceEquals(contact.Second, item));

    internal IEnumerable<GameItem> Neighbors(GameItem item)
    {
        foreach (var (first, second) in _contacts)
        {
            if (ReferenceEquals(first, item))
            {
                yield return second;
            }
            else if (ReferenceEquals(second, item))
            {
                yield return first;
            }
        }
    }
}

internal sealed class GameSlotInventory : GameInventory
{
    internal GameItem? childItem;
    internal SlotMarker? TryFindOneValidInventorySlot(GameItem item) => childItem == null ? new SlotMarker(this) : null;
}

internal static class GeneralHelper
{
    internal static bool IsItemOwned(GameItem item) => item.Owned;
    internal static bool MayOwnershipAcceptItem(GameItem item, GameInventory inventory) => item.Owned;
}

internal static class GraphUtils
{
    internal static T? FindParentType<T>(GameInventory inventory) where T : class => inventory.GetParentItem() as T;
    internal static bool FindParentEquals(GameGridInventory inventory, GraphNodeStorage root) =>
        ReferenceEquals(inventory.parent, root);

    internal static List<T> FindAllChildrenType<T>(GameItem item) =>
        item.Slots.OfType<T>().Concat(item.Children.OfType<T>()).ToList();

    /// <summary>Records native transfer calls; returns the units that did not move, as the game does.</summary>
    internal static readonly List<(GraphNodeStorage? Node, GameItem? Item, int Amount)> NativeTransfers = new();

    internal static int NativeRemaining { get; set; }

    internal static int TryAcceptAll(GraphNodeStorage? node, GameItem? item, int amount = -1)
    {
        NativeTransfers.Add((node, item, amount));
        return NativeRemaining;
    }
}

namespace ProbablyStolen.DigitalStorage.Platform.Client
{
    internal static class CellContentWindow
    {
        internal const string BayPrefix = "digital_storage.cell_bay.";
        internal const string SlotId = "digital_storage.component_slot";

        internal static GameItem? Component(GameItem host) =>
            host.Slots.FirstOrDefault(slot => slot.identifier == SlotId)?.childItem;

        internal static bool IsMounted(GameItem host) =>
            host.parentInventory?.identifier?.StartsWith(BayPrefix, System.StringComparison.Ordinal) == true ||
            ProbablyStolen.DigitalStorage.Platform.Content.IoPortItem.IsCellInBay(host);

        internal static ProbablyStolen.DigitalStorage.Common.Contracts.StorageContentsState StorageState(GameItem host) =>
            ProbablyStolen.DigitalStorage.Common.Contracts.StorageContentsState.Unavailable;

        internal static int ExpansionStage(GameItem host) => 0;

        internal static bool HasModules(GameItem host) => false;

        internal static bool HasPartitionRules(GameItem host) => false;
    }
}

namespace ProbablyStolen.DigitalStorage.Platform.Content
{
    internal static class ControllerItem
    {
        internal const string ItemId = "digital_storage.storage_network_controller";
    }

    internal static class DriveItem
    {
        internal const string ItemId = "digital_storage.storage_drive";
    }

    internal static class TerminalItem
    {
        internal const string ItemId = "digital_storage.storage_terminal";
    }
}
