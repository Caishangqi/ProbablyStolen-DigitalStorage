using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Compat.DsStocking;

/// <summary>
/// Patch methods the integration host applies to DS Stocking Helper. They are plain static methods, never
/// <c>[HarmonyPatch]</c> attributes: the loader runs <c>PatchAll</c> only on the IMod assembly, and an attribute naming
/// an absent mod would break loading.
/// </summary>
public static class DsStockingPatches
{
    private static readonly MethodInfo Original = AccessTools.Method(typeof(GraphUtils), nameof(GraphUtils.TryAcceptAll));
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(DsStockingBridge), nameof(DsStockingBridge.TryAcceptAll));
    private static readonly Dictionary<MethodBase, int> Rewritten = new();
    private static Candidate? _candidate;
    private static MethodInfo? _containerKept;

    internal static Func<bool> OfferStacks { get; set; } = () => true;

    /// <summary>Resolves DS Stocking Helper's candidate type; returns the reason when it does not match.</summary>
    internal static string? Resolve()
    {
        var create = DsStockingTargets.FoundConstructor();
        var item = DsStockingTargets.FoundItem();
        var holder = DsStockingTargets.FoundHolder();
        var where = DsStockingTargets.FoundWhere();
        if (create == null || item == null || holder == null || where == null || item.FieldType != typeof(GameItem) ||
            holder.FieldType != typeof(GameItem) || where.FieldType != typeof(string))
        {
            return "the stocking candidate type changed";
        }

        _candidate = new Candidate(create, item, holder, where);
        // Optional: without it, every drive and cell counts as not kept.
        _containerKept = DsStockingTargets.ContainerKept();
        return null;
    }

    /// <summary>Starts a run before DS Stocking Helper collects and puts up goods.</summary>
    public static void RunPrefix() => DsStockingBridge.BeginRun();

    /// <summary>Ends the run whether it returned or threw, and keeps DS Stocking Helper's own exception.</summary>
    public static Exception? RunFinalizer(Exception? __exception)
    {
        DsStockingBridge.EndRun();
        return __exception;
    }

    /// <summary>
    /// Hands the candidate list of the run's own scan to <see cref="AddNetworkGoods"/>. Other scans (water bottling,
    /// vending refill, highlights) never see network goods, so they never move a stored root natively.
    /// </summary>
    public static IEnumerable<CodeInstruction> AddNetworkGoodsToScan(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var collect = DsStockingTargets.Collect();
        var count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (collect == null || !instruction.Calls(collect)) continue;
            yield return new CodeInstruction(OpCodes.Dup);
            yield return new CodeInstruction(OpCodes.Call, AddNetworkGoodsMethod);
            count++;
        }

        Rewritten[original] = count;
    }

    /// <summary>
    /// Appends the stored roots of online networks in the store to the run's candidates, filtered like DS Stocking
    /// Helper's own scan: owned, visible, not debris or display-only, and not in a drive or cell the player keeps.
    /// Outside the store DS Stocking Helper scans only the player's pockets, so nothing is added.
    /// </summary>
    public static void AddNetworkGoods(object? candidates)
    {
        var shape = _candidate;
        if (shape == null || candidates is not IList list || !DsStockingBridge.IsRunning) return;
        try
        {
            var entry = EmporiumEntry.Instance;
            if (entry == null || IsOutsideStore()) return;
            var present = new HashSet<GameItem>();
            foreach (var existing in list)
            {
                if (shape.Item.GetValue(existing) is GameItem item) present.Add(item);
            }

            foreach (var (root, holder) in DsStockingBridge.StoredGoods(entry.invElement, OfferStacks()))
            {
                if (present.Contains(root) || !IsOffered(root) || IsKept(holder) ||
                    IsKept(root.parentInventory?.GetParentItem())) continue;
                list.Add(shape.Create(root, holder));
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Digital Storage: DS Stocking Helper network goods were not offered ({error.GetType().Name}).");
        }
    }

    /// <summary>Returns a root taken this run to its network instead of to its old slot on a cell page.</summary>
    public static bool PutBackPrefix(GameItem it, ref bool __result)
    {
        if (!DsStockingBridge.TryPutBack(it, out var returned)) return true;
        __result = returned;
        return false;
    }

    /// <summary>Points every <see cref="GraphUtils.TryAcceptAll"/> call of the patched method at the bridge.</summary>
    public static IEnumerable<CodeInstruction> RedirectTransfers(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(Original))
            {
                instruction.operand = Replacement;
                count++;
            }

            yield return instruction;
        }

        Rewritten[original] = count;
    }

    /// <summary>Gets how many calls a transpiler rewrote or followed in a method; 0 when it never ran.</summary>
    internal static int RewrittenIn(MethodBase method) => Rewritten.TryGetValue(method, out var count) ? count : 0;

    private static bool IsOutsideStore()
    {
        try
        {
            return ModHelper.IsOutsideStore();
        }
        catch
        {
            // DS Stocking Helper scans the store when this check throws.
            return false;
        }
    }

    private static bool IsOffered(GameItem root) =>
        GeneralHelper.IsItemEffectivelyOwned(root) && !root.IsTag("ITEM_HIDDEN_TAG") && !DebrisHelper.IsDebris(root) &&
        !GeneralHelper.IsDisplayOnly(root);

    private static bool IsKept(GameItem? container)
    {
        if (container == null || _containerKept == null) return false;
        try
        {
            return _containerKept.Invoke(null, new object[] { container }) is true;
        }
        catch
        {
            return false;
        }
    }

    // Literal type and member names at every lookup, so the mod scanner reads them as reflection on own types.
    internal static MethodInfo RunPrefixMethod => AccessTools.Method(typeof(DsStockingPatches), nameof(RunPrefix));

    internal static MethodInfo RunFinalizerMethod => AccessTools.Method(typeof(DsStockingPatches), nameof(RunFinalizer));

    internal static MethodInfo AddNetworkGoodsToScanMethod =>
        AccessTools.Method(typeof(DsStockingPatches), nameof(AddNetworkGoodsToScan));

    internal static MethodInfo AddNetworkGoodsMethod => AccessTools.Method(typeof(DsStockingPatches), nameof(AddNetworkGoods));

    internal static MethodInfo PutBackPrefixMethod => AccessTools.Method(typeof(DsStockingPatches), nameof(PutBackPrefix));

    internal static MethodInfo RedirectTransfersMethod => AccessTools.Method(typeof(DsStockingPatches), nameof(RedirectTransfers));

    /// <summary>DS Stocking Helper's candidate: the item, the container shown with it, and that container's name.</summary>
    private sealed class Candidate
    {
        private readonly ConstructorInfo _create;
        private readonly FieldInfo _holder;
        private readonly FieldInfo _where;

        internal Candidate(ConstructorInfo create, FieldInfo item, FieldInfo holder, FieldInfo where)
        {
            _create = create;
            Item = item;
            _holder = holder;
            _where = where;
        }

        internal FieldInfo Item { get; }

        internal object Create(GameItem root, GameItem? holder)
        {
            var candidate = _create.Invoke(Array.Empty<object>());
            Item.SetValue(candidate, root);
            _holder.SetValue(candidate, holder);
            _where.SetValue(candidate, Name(holder ?? root));
            return candidate;
        }

        private static string Name(GameItem item)
        {
            try
            {
                var name = item.GetDisplayName();
                return string.IsNullOrEmpty(name) ? item.identifier : name;
            }
            catch
            {
                return item.identifier;
            }
        }
    }
}
