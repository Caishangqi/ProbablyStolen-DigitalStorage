using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Stores a whole stack dropped onto the terminal item itself, without opening the terminal. A drop onto an item
/// first tries the native steps in their order: insert into the item, stack with it, then use the dragged item on it.
/// Storing is the last step. Items without a use of their own reach it through the native "use item on item" hooks
/// (<c>mayItemTargetThisFunc</c>, <c>canItemTargetThisFunc</c>, <c>onItemTargetThisFunc</c>). Items with a use of
/// their own keep it where it applies, such as the blue labeler renaming the terminal; where it does not apply,
/// <see cref="TerminalItemDropRouter"/> stores them once the native drop has left them where they were.
/// </summary>
internal sealed class TerminalItemDrop
{
    // Hover asks every frame; a network scan per frame is too much for one gesture.
    private const float CacheSeconds = 0.25f;
    private static readonly ConditionalWeakTable<GameItem, TerminalItemDrop> Drops = new();
    private readonly GameItem _terminal;
    private readonly Func<IReadOnlyList<GameItem>, int?, IReadOnlyList<string?>> _storeRefusals;
    private readonly Func<GameItem, int, TerminalInsertionResult> _store;
    private readonly ModText _text;
    private readonly Action<string> _log;
    private GameItem? _cachedItem;
    private int _cachedUnits;
    private int _cachedAmount;
    private string? _cachedRefusal;
    private float _cachedUntil;

    internal TerminalItemDrop(GameItem terminal, Func<IReadOnlyList<GameItem>, int?, IReadOnlyList<string?>> storeRefusals,
        Func<GameItem, int, TerminalInsertionResult> store, ModText text, Action<string> log)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _storeRefusals = storeRefusals ?? throw new ArgumentNullException(nameof(storeRefusals));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal GameItem Terminal => _terminal;

    internal void Attach()
    {
        _terminal.mayItemTargetThisFunc += MayTarget;
        _terminal.canItemTargetThisFunc += CanTarget;
        _terminal.onItemTargetThisFunc += Target;
        Drops.Remove(_terminal);
        Drops.Add(_terminal, this);
    }

    /// <summary>Gets the drop handling of a terminal item, or null for any other item.</summary>
    internal static TerminalItemDrop? Find(GameItem? item) =>
        item != null && Drops.TryGetValue(item, out var drop) ? drop : null;

    /// <summary>
    /// Gets whether dropping <paramref name="source"/> here ends in a store attempt: the native insert and stack
    /// steps do not take it, and it either has no use of its own or that use does not apply to the terminal.
    /// </summary>
    internal bool EndsInStore(GameItem source) => Classify(source) switch
    {
        DropKind.Store => !NativeInsertsOrStacks(source),
        DropKind.Defer => !NativeInsertsOrStacks(source) && !(source.MayTarget(_terminal) && source.CanTarget(_terminal)),
        _ => false
    };

    /// <summary>
    /// Gets whether only this mod's fallback stores <paramref name="source"/>: it has a use of its own, and neither
    /// that use nor the native insert and stack steps apply here.
    /// </summary>
    internal bool NeedsFallback(GameItem source) => Classify(source) == DropKind.Defer && EndsInStore(source);

    /// <summary>Gets why <paramref name="source"/> could not be stored right now as a text key, or null.</summary>
    internal string? Refusal(GameItem source, int amount)
    {
        if (ReferenceEquals(source, _cachedItem) && source.unitCount == _cachedUnits && amount == _cachedAmount &&
            Time.unscaledTime < _cachedUntil)
        {
            return _cachedRefusal;
        }

        _cachedItem = source;
        _cachedUnits = source.unitCount;
        _cachedAmount = amount;
        _cachedUntil = Time.unscaledTime + CacheSeconds;
        try
        {
            _cachedRefusal = _storeRefusals(new[] { source }, amount)[0];
        }
        catch
        {
            _cachedRefusal = StoreAdmission.UnsupportedKey;
        }

        return _cachedRefusal;
    }

    /// <summary>Gets whether any of a group's whole stacks could be stored, reading the network once.</summary>
    internal bool AnyStorable(IReadOnlyList<GameItem> items)
    {
        if (items.Count == 0) return false;
        try
        {
            foreach (var refusal in _storeRefusals(items, null))
            {
                if (refusal == null) return true;
            }
        }
        catch
        {
            // A failed check reads as nothing storable; the native highlight stays.
        }

        return false;
    }

    /// <summary>Builds the hover tooltip for dragging <paramref name="source"/> over the terminal.</summary>
    internal RichText HoverHint(GameItem source, int amount)
    {
        var refusal = Refusal(source, amount);
        return TerminalDropHint.Build(_text, 1, refusal == null ? null : source, refusal);
    }

    /// <summary>Stores an item the native drop left in place; see <see cref="NeedsFallback"/>.</summary>
    internal void StoreFallback(GameItem source, int amount) => Store(source, amount, "icon drop fallback");

    // MayTarget/CanTarget need at least one true vote and no false vote across both items and their slots, so a
    // false here would also veto the dragged item's own use on the terminal. Items with a use of their own get a
    // true vote and their own handlers decide; the fallback stores them when their use does not apply.
    private bool MayTarget(GameItem source, GameItem target) => Classify(source) switch
    {
        DropKind.Defer => true,
        DropKind.Store => StoreAdmission.Refusal(source) == null,
        _ => false
    };

    private bool CanTarget(GameItem source, GameItem target) => Classify(source) switch
    {
        DropKind.Defer => true,
        DropKind.Store => Refusal(source, DraggedAmount(source)) == null,
        _ => false
    };

    private void Target(GameItem source, GameItem target)
    {
        if (Classify(source) != DropKind.Store) return;
        Store(source, DraggedAmount(source), "icon drop");
    }

    private void Store(GameItem source, int amount, string label)
    {
        _cachedItem = null;
        try
        {
            // Storage transfers whole roots regardless of the drag quantity; a partial drag is refused.
            var result = _store(source, amount);
            _log($"Digital Storage: {label} status={result.Status} units={source.unitCount} dragged={amount} stored={result.StoredAmount}");
        }
        catch (Exception error)
        {
            _log($"Digital Storage: {label} failed error={error.GetType().Name}");
        }
        if (!NativeItemMove.IsSafe(source))
        {
            Debug.LogError("Digital Storage: icon drop aborted; item ownership is unconfirmed.");
            ItemMove.RequireSafeDrag(false);
        }
    }

    private bool NativeInsertsOrStacks(GameItem source) =>
        GeneralHelper.MayPlayerInsertInto(_terminal) && _terminal.MayHaveValidInventorySlot(source) ||
        _terminal.MayStack(source);

    /// <summary>Gets the quantity the player is dragging; outside a native single drag it is the whole stack.</summary>
    private static int DraggedAmount(GameItem source)
    {
        var drag = ItemMouseDragHandler.current;
        return drag?.IsDraggingItem == true && ReferenceEquals(drag.GetCurrentItem(), source)
            ? NativeTerminalDragAccess.GetAmount(drag)
            : source.unitCount;
    }

    private DropKind Classify(GameItem? source)
    {
        if (source == null || ReferenceEquals(source, _terminal) || IsInsideTerminal(source)) return DropKind.None;
        if (source.mayThisTargetItemFunc != null || source.mayItemTargetItemFunc != null) return DropKind.Defer;
        // Modules go to the terminal's module bay through the native insertion step that runs before targeting.
        return source.IsTag("MODULE_TAG") ? DropKind.None : DropKind.Store;
    }

    private bool IsInsideTerminal(GameItem source)
    {
        for (var parent = Owner(source); parent != null; parent = Owner(parent))
        {
            if (ReferenceEquals(parent, _terminal)) return true;
        }

        return false;
    }

    private static GameItem? Owner(GameItem item) =>
        item.parentInventory == null ? null : GraphUtils.FindParentType<GameItem>(item.parentInventory);

    private enum DropKind
    {
        None,
        Defer,
        Store
    }
}
