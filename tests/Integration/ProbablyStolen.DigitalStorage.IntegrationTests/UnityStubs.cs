// Unity members the linked sources touch; the tests need no engine.
namespace UnityEngine
{
    internal readonly struct Vector2
    {
        internal static Vector2 zero => default;
    }

    internal static class Object
    {
        internal static void Destroy(object gameObject) { }
    }
}

// StockingTargets names Customer Stocking members through HarmonyLib, which the core test host does not load.
namespace ProbablyStolen.DigitalStorage.Compat.CustomerStocking
{
    internal static class StockingTargets
    {
        internal const string ModId = "customerstocking";
    }
}

// DsStockingTargets names DS Stocking Helper members through HarmonyLib, which the core test host does not load.
namespace ProbablyStolen.DigitalStorage.Compat.DsStocking
{
    internal static class DsStockingTargets
    {
        internal const string ModId = "ds_stocking";
    }
}
