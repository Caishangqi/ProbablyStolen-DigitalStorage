using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Compat.CustomerStocking;

/// <summary>
/// External member names must be literal and namespaced; computed names count as <c>reflection-external</c>.
/// </summary>
internal static class StockingTargets
{
    /// <summary>Gets the mod id from Customer Stocking's manifest.</summary>
    internal const string ModId = "customerstocking";

    /// <summary>Gets the versions this integration was tested with.</summary>
    internal static readonly string[] VerifiedVersions = { "1.6.3" };

    /// <summary>Gets the one-click stocking entry, shared by its button and its hotkey; null when it is missing.</summary>
    internal static MethodBase? Run() => AccessTools.Method("CustomerStocking.AutoStocker:Run");

    /// <summary>Gets the method that returns units to their inventory when the budget is exceeded; null when missing.</summary>
    internal static MethodBase? RestoreBackCountDelta() =>
        AccessTools.Method("CustomerStocking.AutoStocker:RestoreBackCountDelta");
}
