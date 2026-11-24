using System.Collections.Generic;

// Linked Compat sources log through Unity; the tests record the lines instead.
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

    internal static class Debug
    {
        internal static readonly List<string> Lines = new();

        internal static void Log(object message) => Lines.Add(message?.ToString() ?? string.Empty);

        internal static void LogWarning(object message) => Lines.Add(message?.ToString() ?? string.Empty);

        internal static void LogError(object message) => Lines.Add(message?.ToString() ?? string.Empty);
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
