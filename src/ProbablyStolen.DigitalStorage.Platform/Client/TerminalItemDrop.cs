using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Stores a whole stack dropped onto the terminal item itself, without opening the terminal. It uses the native
/// "use item on item" hooks (<c>mayItemTargetThisFunc</c>, <c>canItemTargetThisFunc</c>, <c>onItemTargetThisFunc</c>),
/// which <c>ItemMouseDragHandler.EndDrag</c> reaches after child inventory insertion and stacking.
/// </summary>
internal sealed class TerminalItemDrop
{
    // Hover asks MayTarget/CanTarget every frame; a network scan per frame is too much for one gesture.
    private const float CacheSeconds = 0.25f;
    private readonly GameItem _terminal;
    private readonly Func<GameItem, int, bool> _canStore;
    private readonly Func<GameItem, int, TerminalInsertionResult> _store;
    private readonly Action<string> _log;
    private GameItem? _cachedItem;
    private int _cachedUnits;
    private int _cachedAmount;
    private bool _cachedResult;
    private float _cachedUntil;

    internal TerminalItemDrop(GameItem terminal, Func<GameItem, int, bool> canStore,
        Func<GameItem, int, TerminalInsertionResult> store, Action<string> log)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _canStore = canStore ?? throw new ArgumentNullException(nameof(canStore));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal void Attach()
    {
        _terminal.mayItemTargetThisFunc += MayTarget;
        _terminal.canItemTargetThisFunc += CanTarget;
        _terminal.onItemTargetThisFunc += Target;
    }

    // MayTarget/CanTarget need at least one true vote and no false vote across both items and their slots, so a
    // false here would also veto the dragged item's own target behaviour on the terminal. Such items get a true vote
    // and their own handlers decide; they are never stored this way.
    private bool MayTarget(GameItem source, GameItem target) => Classify(source) switch
    {
        DropKind.Defer => true,
        DropKind.Store => StorageService.IsStorable(source),
        _ => false
    };

    private bool CanTarget(GameItem source, GameItem target) => Classify(source) switch
    {
        DropKind.Defer => true,
        DropKind.Store => CanStoreCached(source),
        _ => false
    };

    private void Target(GameItem source, GameItem target)
    {
        if (Classify(source) != DropKind.Store) return;
        var amount = DraggedAmount(source);
        _cachedItem = null;
        try
        {
            // Target receives the whole dragged item without the chosen quantity; the store moves whole roots only.
            var result = _store(source, amount);
            _log($"Digital Storage: icon drop status={result.Status} units={source.unitCount} dragged={amount} stored={result.StoredAmount}");
        }
        catch (Exception error)
        {
            _log($"Digital Storage: icon drop failed error={error.GetType().Name}");
        }
        if (!NativeItemMove.IsSafe(source))
        {
            Debug.LogError("Digital Storage: icon drop aborted; item ownership is unconfirmed.");
            ItemMove.RequireSafeDrag(false);
        }
    }

    private bool CanStoreCached(GameItem source)
    {
        var amount = DraggedAmount(source);
        if (ReferenceEquals(source, _cachedItem) && source.unitCount == _cachedUnits && amount == _cachedAmount &&
            Time.unscaledTime < _cachedUntil)
        {
            return _cachedResult;
        }

        _cachedItem = source;
        _cachedUnits = source.unitCount;
        _cachedAmount = amount;
        _cachedUntil = Time.unscaledTime + CacheSeconds;
        try
        {
            _cachedResult = _canStore(source, amount);
        }
        catch
        {
            _cachedResult = false;
        }

        return _cachedResult;
    }

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
