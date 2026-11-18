using System;
using System.Collections.Generic;
using System.Linq;

// Physical boundary doubles for the linked production scanner. They do not simulate native shape or Unity behavior.
internal sealed class GameItemState
{
    internal readonly Dictionary<string, object?> dict = new();
}

internal sealed class GameItemShape
{
    internal int width;
    internal int height;
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
}

internal static class SaveManager
{
    internal static bool IsDecoding { get; set; }
}

public sealed class TagState
{
    public int valueInt;
    public bool valueEnabled;
    public bool GetBool() => valueEnabled;
    public TagState Clone() => new() { valueInt = valueInt, valueEnabled = valueEnabled };
    public TagState Enable() { valueEnabled = true; return this; }
    public TagState SetInt(int value) { valueInt = value; return this; }
}

public sealed class TagSystem
{
    public Dictionary<string, TagState> dict = new();
    public TagSystem Clone() => new() { dict = new Dictionary<string, TagState>(dict) };
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
}

public class GameItem
{
    internal GameItem(string id)
    {
        identifier = id;
        state = new GameItemState();
        Stuck = false;
    }

    internal string identifier;
    internal GameItemState? state;
    internal TagSystem modifiedState = new();
    internal GameInventory? parentInventory;
    internal readonly List<GameSlotInventory> Slots = new();
    internal int unitCount = 1;
    internal GameItemShape shape = new() { width = 1, height = 2 };
    internal bool Stuck = false;
    internal bool TooltipRefusal = false;
    internal bool ThrowTooltip = false;
    internal bool ReceivingModuleBonus = false;
    internal bool ContainerTooltip = false;
    internal bool Owned = true;
    internal readonly List<GameItem> Children = new();
    internal readonly List<GameGridInventory> StoragePages = new();

    internal bool IsTag(string tag) => tag == "CONTAINER_TAG" || tag == "CONTAINER_TOOLTIP_TAG"
        ? ContainerTooltip
        : Stuck && tag == ModuleHelper.MODULE_STUCK_TAG;
    internal TagState GetTagReadonly(string tag) => ReceivingModuleBonus && tag == "IS_RECEIVING_BONUS_FROM_MODULE_BOOL"
        ? new TagState().Enable()
        : new TagState();

    internal List<T> FindAdjacentType<T>() => parentInventory is GameGridInventory grid
        ? grid.Neighbors(this).OfType<T>().ToList()
        : new List<T>();
}

/// <summary>Stands in for the native graph node base type.</summary>
public class GraphNodeStorage
{
}

public class GameInventory : GraphNodeStorage
{
    internal string identifier = string.Empty;
    internal GameItem? Owner;
    internal readonly List<GameItem> childItems = new();

    internal void Place(GameItem item)
    {
        item.parentInventory?.childItems.Remove(item);
        item.parentInventory = this;
        childItems.Add(item);
    }
}

public sealed class GameGridInventory : GameInventory
{
    internal bool overrideLockInsert;
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

internal sealed class GameSlotInventory
{
    internal string? identifier;
    internal GameItem? childItem;
}

internal static class GeneralHelper
{
    internal static bool IsItemOwned(GameItem item) => item.Owned;
}

internal static class GraphUtils
{
    internal static T? FindParentType<T>(GameInventory inventory) where T : class => inventory.Owner as T;

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
            host.parentInventory?.identifier?.StartsWith(BayPrefix, System.StringComparison.Ordinal) == true;

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
