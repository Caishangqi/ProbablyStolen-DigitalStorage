using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Compat.CustomerStocking;

/// <summary>
/// Every Customer Stocking member this integration uses. Names are literal and namespaced: computed names count as
/// <c>reflection-external</c>. Since 1.6.4 one-click stocking is a coroutine (<c>AutoStocker.RunAsync</c>): it scans
/// candidates in <c>BuildPlan</c> on its first frame, then places them on later frames in the iterator
/// <c>ExecutePlan</c>, whose body (with the transfer call) lives in the compiler-generated <c>MoveNext</c>.
/// </summary>
internal static class StockingTargets
{
    /// <summary>Gets the mod id from Customer Stocking's manifest.</summary>
    internal const string ModId = "customerstocking";

    /// <summary>Gets the versions this integration was tested with.</summary>
    internal static readonly string[] VerifiedVersions = { "1.6.4" };

    /// <summary>Gets the stocking coroutine started by the button and the hotkey; null when it is missing.</summary>
    internal static MethodBase? RunAsync() => AccessTools.Method("CustomerStocking.AutoStocker:RunAsync");

    /// <summary>Gets the synchronous candidate scan of a run; null when it is missing.</summary>
    internal static MethodBase? BuildPlan() => AccessTools.Method("CustomerStocking.AutoStocker:BuildPlan");

    /// <summary>Gets the body of the placement iterator, which moves the chosen items; null when it is missing.</summary>
    internal static MethodBase? ExecutePlanBody()
    {
        var executePlan = AccessTools.Method("CustomerStocking.AutoStocker:ExecutePlan");
        return executePlan == null ? null : AccessTools.EnumeratorMoveNext(executePlan);
    }

    /// <summary>Gets the method that returns units to their inventory when the budget is exceeded; null when missing.</summary>
    internal static MethodBase? RestoreBackCountDelta() =>
        AccessTools.Method("CustomerStocking.AutoStocker:RestoreBackCountDelta");
}
