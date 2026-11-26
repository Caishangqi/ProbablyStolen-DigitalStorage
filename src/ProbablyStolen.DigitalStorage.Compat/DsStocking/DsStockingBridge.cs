using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Storage;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Compat.DsStocking;

/// <summary>
/// The DS Stocking Helper side of the public API. A stocking run is one synchronous call, so it runs inside one
/// external access scope: stored roots pass DS Stocking Helper's removal checks, and the cells that changed are compacted
/// when the run ends. DS Stocking Helper's own scan never opens the hidden cell pages, so the run's candidates get the
/// stored roots of online networks in the store (<see cref="StoredGoods"/>). Its moves are redirected here during a run:
/// <list type="bullet">
/// <item>a stored root goes to the counter whole through <see cref="IStorage.ExtractRoot"/>, even when one unit was
/// asked for, because the API never splits a stack;</item>
/// <item>a root taken this run that is sent back goes home whole through <see cref="IStorage.Insert"/>.</item>
/// </list>
/// Every other call goes to <see cref="GraphUtils.TryAcceptAll"/> unchanged.
/// </summary>
/// <remarks>Main thread only, like the stocking button and hotkey that run it.</remarks>
public static class DsStockingBridge
{
    private static readonly IActionSource Source = ActionSource.OfMod(DsStockingTargets.ModId);

    // Roots taken this run and their network, and the pages they left: a send-back never moves anything natively onto
    // a cell page.
    private static readonly Dictionary<GameItem, IGridNode> Taken = new();
    private static readonly HashSet<GameInventory> SourcePages = new();
    private static IDigitalStorageRuntime? _runtime;
    private static IDisposable? _scope;
    private static bool _running;

    internal static bool IsRunning => _running;

    /// <summary>Sets the runtime the bridge uses; null detaches it and ends a run.</summary>
    internal static void Attach(IDigitalStorageRuntime? runtime)
    {
        EndRun();
        _runtime = runtime;
    }

    /// <summary>Starts a run; without a runtime every move stays native.</summary>
    internal static void BeginRun()
    {
        EndRun();
        if (_runtime == null) return;
        try
        {
            _scope = _runtime.ExternalAccess.Open(Source);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: DS Stocking Helper run could not see the network ({error.GetType().Name}).");
            return;
        }

        _running = true;
    }

    /// <summary>Ends the run: forgets taken roots and closes the scope, which compacts the cells that changed.</summary>
    internal static void EndRun()
    {
        _running = false;
        Taken.Clear();
        SourcePages.Clear();
        var scope = _scope;
        _scope = null;
        try
        {
            scope?.Dispose();
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: DS Stocking Helper run did not end cleanly ({error.GetType().Name}).");
        }
    }

    /// <summary>
    /// Lists the stored roots of the online networks that have a machine in <paramref name="store"/>, each with the
    /// item that holds it for display and container rules: the drive, or the cell when it is not in a drive. A network
    /// that cannot be read adds nothing. Empty outside a run.
    /// </summary>
    /// <param name="store">The store inventory DS Stocking Helper scans.</param>
    /// <param name="stacks">Whether roots of more than one unit are offered; they always move whole.</param>
    internal static List<(GameItem Root, GameItem? Holder)> StoredGoods(GraphNodeStorage? store, bool stacks)
    {
        var goods = new List<(GameItem, GameItem?)>();
        var runtime = _runtime;
        if (!_running || runtime == null || store == null) return goods;
        var seen = new HashSet<GameItem>();
        var roots = new List<GameItem>();
        try
        {
            foreach (var grid in runtime.Grids.FindOnlineGrids(store))
            {
                roots.Clear();
                var status = grid.GetStorageService().GetInventory().GetStoredRoots(roots);
                if (status != StorageStatus.Done)
                {
                    Debug.Log($"Digital Storage: DS Stocking Helper skipped a network ({status}).");
                    continue;
                }

                foreach (var root in roots)
                {
                    if (root == null || root.unitCount <= 0 || (!stacks && root.unitCount > 1) || !seen.Add(root)) continue;
                    goods.Add((root, HolderOf(root)));
                }
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: DS Stocking Helper could not list network goods ({error.GetType().Name}).");
        }

        return goods;
    }

    /// <summary>
    /// Replaces <see cref="GraphUtils.TryAcceptAll"/> inside DS Stocking Helper's stocking methods, with the same
    /// contract: moves up to <paramref name="amount"/> units (all units when negative) and returns the units that did
    /// NOT move. A stored root moves whole, so more units than asked can move; the result is then 0.
    /// </summary>
    public static int TryAcceptAll(GraphNodeStorage currentNode, GameItem item, int amount)
    {
        var runtime = _runtime;
        if (runtime == null || item == null || currentNode == null || !_running)
            return GraphUtils.TryAcceptAll(currentNode, item, amount);

        var requested = amount < 0 ? item.unitCount : amount;
        if (requested <= 0) return GraphUtils.TryAcceptAll(currentNode, item, amount);
        try
        {
            if (Taken.TryGetValue(item, out var home))
            {
                // Only whole roots return; part of one stays on the counter, still owned by the player.
                return requested < item.unitCount ? requested : Math.Max(0, requested - Return(home, item));
            }

            if (runtime.Grids.TryGetNodeOfStored(item, out var node) && node != null)
                return Math.Max(0, requested - Extract(node, item, currentNode));
            // Part of a merged counter stack sent back to the page its root left: it stays on the counter.
            if (currentNode is GameInventory page && SourcePages.Contains(page)) return requested;
        }
        catch (Exception error)
        {
            // Nothing moved through the API when it threw before its transfer, and a transfer never half-moves a unit.
            Debug.LogWarning($"Digital Storage: DS Stocking Helper move of {item.identifier} failed ({error.GetType().Name}).");
            return requested;
        }

        return GraphUtils.TryAcceptAll(currentNode, item, amount);
    }

    /// <summary>
    /// Sends a whole root taken this run back to its network, in place of DS Stocking Helper's return to the old slot.
    /// </summary>
    /// <returns>False when the root was not taken this run, so the original return runs.</returns>
    internal static bool TryPutBack(GameItem item, out bool returned)
    {
        returned = false;
        if (!_running || item == null || !Taken.TryGetValue(item, out var home)) return false;
        try
        {
            returned = Return(home, item) > 0;
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: DS Stocking Helper return of {item.identifier} failed ({error.GetType().Name}).");
        }

        return true;
    }

    private static int Extract(IGridNode node, GameItem item, GraphNodeStorage target)
    {
        // The counter is a grid inventory; anything else is not a stocking target this bridge knows.
        if (target is not GameGridInventory destination) return 0;
        var page = item.parentInventory;
        var units = item.unitCount;
        var result = node.Grid.GetStorageService().GetInventory()
            .ExtractRoot(item, units, destination, Actionable.Modulate, Source);
        if (result.Units > 0)
        {
            Taken[item] = node;
            if (page != null) SourcePages.Add(page);
        }
        else if (result.Status is StorageStatus.Unavailable or StorageStatus.Rejected)
        {
            Debug.Log($"Digital Storage: DS Stocking Helper could not take {item.identifier} ({result.Status}).");
        }

        return (int)Math.Min(result.Units, int.MaxValue);
    }

    private static int Return(IGridNode home, GameItem item)
    {
        var units = item.unitCount;
        var result = home.Grid.GetStorageService().GetInventory().Insert(item, units, Actionable.Modulate, Source);
        if (result.Status == StorageStatus.Done)
        {
            Taken.Remove(item);
            return units;
        }

        // The root stays on the counter, still owned by the player; DS Stocking Helper reports it as put up.
        Debug.Log($"Digital Storage: DS Stocking Helper could not return {item.identifier} to the network ({result.Status}).");
        return 0;
    }

    private static GameItem? HolderOf(GameItem root)
    {
        // Pages sit in the cell's window; a mounted cell sits in a drive bay.
        var cell = root.parentInventory?.GetParentItem();
        return cell?.parentInventory?.GetParentItem() ?? cell;
    }
}
