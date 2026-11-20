using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Platform.Integration;

/// <summary>Runtime switch availability consumed by the native mod settings patch.</summary>
public static class IntegrationSettingsView
{
    private static readonly Dictionary<(string ModId, string Key), SwitchState> Switches = new();

    /// <summary>Notifies native settings adapters when a switch state or its status label changes.</summary>
    public static event Action<string, string, bool, string?>? SwitchStateChanged;

    /// <summary>Returns whether a generated integration toggle must be read-only.</summary>
    public static bool IsSwitchLocked(string modId, string key) =>
        Switches.TryGetValue((modId, key), out var state) && state.Locked;

    /// <summary>Returns whether a config entry is an integration switch.</summary>
    public static bool IsIntegrationSwitch(string modId, string key) => Switches.ContainsKey((modId, key));

    internal static void SetSwitchState(string modId, string key, bool locked, string? labelStateKey)
    {
        var id = (modId, key);
        if (!Switches.TryGetValue(id, out var state))
        {
            state = new SwitchState();
            Switches.Add(id, state);
        }

        var changed = state.Locked != locked || !string.Equals(state.LabelStateKey, labelStateKey, StringComparison.Ordinal);
        state.Locked = locked;
        state.LabelStateKey = labelStateKey;
        if (changed) Notify(modId, key, locked, labelStateKey);
    }

    internal static void ClearMod(string modId)
    {
        foreach (var entry in Switches.Where(pair => pair.Key.ModId == modId).ToArray())
        {
            Switches.Remove(entry.Key);
            Notify(entry.Key.ModId, entry.Key.Key, locked: false, labelStateKey: null);
        }
    }

    private static void Notify(string modId, string key, bool locked, string? labelStateKey)
    {
        var handlers = SwitchStateChanged;
        if (handlers == null) return;
        foreach (Action<string, string, bool, string?> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(modId, key, locked, labelStateKey);
            }
            catch
            {
                // A settings view is optional and must not break integration state changes.
            }
        }
    }

    private sealed class SwitchState
    {
        internal bool Locked { get; set; }
        internal string? LabelStateKey { get; set; }
    }
}
