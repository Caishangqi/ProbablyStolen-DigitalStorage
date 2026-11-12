using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Storage;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Compat.CustomerStocking;

/// <summary>
/// The Customer Stocking side of the public API. While one-click stocking runs, an external access scope is open, so
/// Customer Stocking's own scan and filters see roots of online networks. Its two moves are redirected here:
/// <list type="bullet">
/// <item>a root that sits on a cell page moves to the back counter through <see cref="IStorage.ExtractRoot"/>;</item>
/// <item>a budget rollback back to a cell page stores the item through <see cref="IStorage.Insert"/>.</item>
/// </list>
/// Every other call goes to <see cref="GraphUtils.TryAcceptAll"/> unchanged.
/// </summary>
/// <remarks>Main thread only, like the stocking button and hotkey that run it.</remarks>
public static class StockingBridge
{
    private static readonly IActionSource Source = ActionSource.OfMod(StockingTargets.ModId);

    // Pages roots left during this session, and their network; a rollback to one of these pages returns to that network.
    private static readonly Dictionary<GameInventory, IGridNode> SourcePages = new();
    private static IDigitalStorageRuntime? _runtime;

    /// <summary>Sets the runtime the bridge uses; null detaches it.</summary>
    internal static void Attach(IDigitalStorageRuntime? runtime)
    {
        _runtime = runtime;
        SourcePages.Clear();
    }

    /// <summary>Opens the session scope; returns null when no runtime is attached or the scope cannot open.</summary>
    internal static IDisposable? OpenSession()
    {
        SourcePages.Clear();
        try
        {
            return _runtime?.ExternalAccess.Open(Source);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: Customer Stocking session could not start ({error.GetType().Name}).");
            return null;
        }
    }

    /// <summary>Closes the session scope; the scope compacts the cells that changed.</summary>
    internal static void CloseSession(IDisposable? session)
    {
        SourcePages.Clear();
        try
        {
            session?.Dispose();
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: Customer Stocking session did not close cleanly ({error.GetType().Name}).");
        }
    }

    /// <summary>
    /// Replaces <see cref="GraphUtils.TryAcceptAll"/> inside Customer Stocking's stocking methods, with the same
    /// contract: moves up to <paramref name="amount"/> units (all units when negative) and returns the units that did
    /// NOT move.
    /// </summary>
    public static int TryAcceptAll(GraphNodeStorage currentNode, GameItem item, int amount)
    {
        var runtime = _runtime;
        if (runtime == null || item == null || currentNode == null || !runtime.ExternalAccess.IsOpen)
            return GraphUtils.TryAcceptAll(currentNode, item, amount);

        var requested = amount < 0 ? item.unitCount : amount;
        if (requested <= 0) return GraphUtils.TryAcceptAll(currentNode, item, amount);
        try
        {
            if (runtime.Grids.TryGetNodeOfStored(item, out var node) && node != null)
                return requested - Extract(node, item, requested, currentNode);
            if (currentNode is GameInventory page && SourcePages.TryGetValue(page, out var home))
                return requested - Return(home, item, requested);
        }
        catch (Exception error)
        {
            // Nothing moved through the API when it threw before its transfer, and a transfer never half-moves a unit.
            Debug.LogWarning($"Digital Storage: Customer Stocking move of {item.identifier} failed ({error.GetType().Name}).");
            return requested;
        }

        return GraphUtils.TryAcceptAll(currentNode, item, amount);
    }

    private static int Extract(IGridNode node, GameItem item, int requested, GraphNodeStorage target)
    {
        // The back counter is a grid inventory; anything else is not a stocking target this bridge knows.
        if (target is not GameGridInventory destination) return 0;
        var page = item.parentInventory;
        var result = node.Grid.GetStorageService().GetInventory()
            .ExtractRoot(item, requested, destination, Actionable.Modulate, Source);
        if (result.Units > 0 && page != null) SourcePages[page] = node;
        if (result.Status is StorageStatus.Unavailable or StorageStatus.Rejected && result.Units == 0)
            Debug.Log($"Digital Storage: Customer Stocking could not take {item.identifier} ({result.Status}).");
        return (int)Math.Min(result.Units, requested);
    }

    private static int Return(IGridNode home, GameItem item, int requested)
    {
        // Only whole roots return: a rollback of part of a merged stack is left on the counter, still owned by the
        // player, and Customer Stocking reports that it could not roll back.
        if (requested != item.unitCount) return 0;
        var result = home.Grid.GetStorageService().GetInventory().Insert(item, requested, Actionable.Modulate, Source);
        if (result.Status != StorageStatus.Done)
            Debug.Log($"Digital Storage: Customer Stocking could not return {item.identifier} to the network ({result.Status}).");
        return (int)Math.Min(result.Units, requested);
    }
}
