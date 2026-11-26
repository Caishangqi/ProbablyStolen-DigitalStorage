using System.Reflection;
using HarmonyLib;

namespace ProbablyStolen.DigitalStorage.Compat.DsStocking;

/// <summary>
/// Every DS Stocking Helper member this integration uses. Names are literal and namespaced: computed names count as
/// <c>reflection-external</c>. One-click stocking is synchronous: <c>Stocker.Run</c> collects candidates with
/// <c>Sources.Collect</c>, puts them up one unit at a time in <c>PutUp</c>, and sends back what went over the budget
/// through <c>SendBack</c> (part of a stack) or <c>PutBack</c> (a whole item, to its old slot first).
/// </summary>
internal static class DsStockingTargets
{
    /// <summary>Gets the mod id from DS Stocking Helper's manifest.</summary>
    internal const string ModId = "ds_stocking";

    /// <summary>Gets the versions this integration was tested with.</summary>
    internal static readonly string[] VerifiedVersions = { "0.4.10" };

    /// <summary>Gets the stocking run behind the button and the hotkey; null when it is missing.</summary>
    internal static MethodBase? Run() => AccessTools.Method("DsStocking.Stocker:Run");

    /// <summary>Gets the candidate scan; null when it is missing.</summary>
    internal static MethodInfo? Collect() => AccessTools.Method("DsStocking.Sources:Collect");

    /// <summary>Gets the method that moves chosen units to the counter; null when it is missing.</summary>
    internal static MethodBase? PutUp() => AccessTools.Method("DsStocking.Stocker:PutUp");

    /// <summary>Gets the rollback of the units a stack added to the counter; null when it is missing.</summary>
    internal static MethodBase? SendBack() => AccessTools.Method("DsStocking.Stocker:SendBack");

    /// <summary>Gets the rollback of a whole item to its old slot; null when it is missing.</summary>
    internal static MethodBase? PutBack() => AccessTools.Method("DsStocking.Stocker:PutBack");

    /// <summary>
    /// Gets the parameterless constructor of the candidate type the scan returns a list of; null when it is missing.
    /// The mod scanner only accepts a type lookup whose literal name is in the same call, so the type is never
    /// passed around.
    /// </summary>
    internal static ConstructorInfo? FoundConstructor() => AccessTools.Constructor(AccessTools.TypeByName("DsStocking.Found"));

    internal static FieldInfo? FoundItem() => AccessTools.Field("DsStocking.Found:Item");

    internal static FieldInfo? FoundHolder() => AccessTools.Field("DsStocking.Found:Holder");

    internal static FieldInfo? FoundWhere() => AccessTools.Field("DsStocking.Found:Where");

    /// <summary>Gets the player's "keep what is in this container" rule; null when it is missing.</summary>
    internal static MethodInfo? ContainerKept() =>
        AccessTools.Method("DsStocking.KeepRules:ContainerKept", new[] { typeof(GameItem) });
}
