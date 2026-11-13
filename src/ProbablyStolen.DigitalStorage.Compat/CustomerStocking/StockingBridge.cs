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
/// The Customer Stocking side of the public API. A stocking run (one coroutine, several frames) is one external access
/// batch, so cell pages are compacted only when the run completes. Its synchronous candidate scan runs inside an
/// external access scope, so Customer Stocking's own scan and filters see roots of online networks. Its two moves are
/// redirected here while the run is active:
/// <list type="bullet">
/// <item>a root that sits on a cell page moves to the back counter through <see cref="IStorage.ExtractRoot"/>;</item>
/// <item>a budget rollback back to a cell page stores the item through <see cref="IStorage.Insert"/>.</item>
/// </list>
/// Every other call goes to <see cref="GraphUtils.TryAcceptAll"/> unchanged.
/// </summary>
/// <remarks>Main thread only, like the stocking button and hotkey that run it. Runs do not overlap: Customer Stocking
/// refuses a new run while one is active.</remarks>
public static class StockingBridge
{
    private static readonly IActionSource Source = ActionSource.OfMod(StockingTargets.ModId);

    // Pages roots left during this session, and their network; a rollback to one of these pages returns to that network.
    private static readonly Dictionary<GameInventory, IGridNode> SourcePages = new();
    private static IDigitalStorageRuntime? _runtime;
    private static IDisposable? _batch;
    private static bool _running;
    private static bool _scanned;

    /// <summary>Gets whether a stocking run is active.</summary>
    internal static bool IsRunning => _running;

    /// <summary>Sets the runtime the bridge uses; null detaches it and ends a run.</summary>
    internal static void Attach(IDigitalStorageRuntime? runtime)
    {
        EndRun();
        _runtime = runtime;
    }

    /// <summary>
    /// Starts a run and returns the completion callback to hand to the coroutine: it ends the run, then calls the
    /// original callback. While a run is active the coroutine refuses to start again (<c>AutoStocker.RunAsync</c> exits
    /// at once and never completes), so a second start keeps the active run and its callback unchanged. A run ends when
    /// it completes or when a game loads. A started coroutine scans in its first frame, so an active run that never
    /// scanned belongs to a coroutine that never started (for example <c>StartCoroutine</c> failed); it is ended first.
    /// </summary>
    internal static Action<string>? StartRun(Action<string>? completed)
    {
        if (_running && !_scanned) EndRun();
        if (_running || _runtime == null) return completed;
        try
        {
            _batch = _runtime.ExternalAccess.Batch(Source);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: Customer Stocking run could not start ({error.GetType().Name}).");
            return completed;
        }

        _running = true;
        return message =>
        {
            EndRun();
            completed?.Invoke(message);
        };
    }

    /// <summary>Ends the run: forgets source pages and ends the batch, which compacts the cells that changed.</summary>
    internal static void EndRun()
    {
        _running = false;
        _scanned = false;
        SourcePages.Clear();
        var batch = _batch;
        _batch = null;
        try
        {
            batch?.Dispose();
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: Customer Stocking run did not end cleanly ({error.GetType().Name}).");
        }
    }

    /// <summary>Opens the scan scope of a run; returns null outside a run or when the scope cannot open.</summary>
    internal static IDisposable? OpenScan()
    {
        if (!_running || _runtime == null) return null;
        _scanned = true;
        try
        {
            return _runtime.ExternalAccess.Open(Source);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: Customer Stocking scan could not see the network ({error.GetType().Name}).");
            return null;
        }
    }

    /// <summary>Closes the scan scope.</summary>
    internal static void CloseScan(IDisposable? scan)
    {
        try
        {
            scan?.Dispose();
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: Customer Stocking scan did not close cleanly ({error.GetType().Name}).");
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
        if (runtime == null || item == null || currentNode == null || !_running)
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
