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

    /// <summary>Opens the session before one-click stocking runs.</summary>
    public static void SessionPrefix(out IDisposable? __state) => __state = StockingBridge.OpenSession();

    /// <summary>Closes the session whether stocking returned or threw, and keeps Customer Stocking's own exception.</summary>
    public static Exception? SessionFinalizer(Exception? __exception, IDisposable? __state)
    {
        StockingBridge.CloseSession(__state);
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
    internal static MethodInfo SessionPrefixMethod => AccessTools.Method(typeof(StockingPatches), nameof(SessionPrefix));

    internal static MethodInfo SessionFinalizerMethod => AccessTools.Method(typeof(StockingPatches), nameof(SessionFinalizer));

    internal static MethodInfo RedirectTransfersMethod => AccessTools.Method(typeof(StockingPatches), nameof(RedirectTransfers));
}
