using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Compat.CustomerStocking;

/// <summary>
/// Patch methods the integration host applies to Customer Stocking. They are plain static methods, never
/// <c>[HarmonyPatch]</c> attributes: the loader runs <c>PatchAll</c> only on the IMod assembly, and an attribute naming
/// an absent mod would break loading.
/// </summary>
public static class StockingPatches
{
    private static readonly MethodInfo Original = AccessTools.Method(typeof(GraphUtils), nameof(GraphUtils.TryAcceptAll));
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(StockingBridge), nameof(StockingBridge.TryAcceptAll));
    private static readonly Dictionary<MethodBase, int> Rewritten = new();

    /// <summary>
    /// Starts a run when the stocking coroutine is created, and makes its completion callback end the run. The
    /// coroutine calls that callback after its own deferred inventory callbacks were flushed.
    /// </summary>
    public static void RunPrefix(ref Action<string>? completed) => completed = StockingBridge.StartRun(completed);

    /// <summary>Lets the candidate scan see online network goods, for this synchronous call only.</summary>
    public static void ScanPrefix(out IDisposable? __state) => __state = StockingBridge.OpenScan();

    /// <summary>Closes the scan scope whether the scan returned or threw, and keeps Customer Stocking's own exception.</summary>
    public static Exception? ScanFinalizer(Exception? __exception, IDisposable? __state)
    {
        StockingBridge.CloseScan(__state);
        return __exception;
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

    /// <summary>Gets how many transfer calls the transpiler rewrote in a method; 0 when it never ran.</summary>
    internal static int RewrittenIn(MethodBase method) => Rewritten.TryGetValue(method, out var count) ? count : 0;

    // Literal type and member names at every lookup, so the mod scanner reads them as reflection on own types.
    internal static MethodInfo RunPrefixMethod => AccessTools.Method(typeof(StockingPatches), nameof(RunPrefix));

    internal static MethodInfo ScanPrefixMethod => AccessTools.Method(typeof(StockingPatches), nameof(ScanPrefix));

    internal static MethodInfo ScanFinalizerMethod => AccessTools.Method(typeof(StockingPatches), nameof(ScanFinalizer));

    internal static MethodInfo RedirectTransfersMethod => AccessTools.Method(typeof(StockingPatches), nameof(RedirectTransfers));
}
