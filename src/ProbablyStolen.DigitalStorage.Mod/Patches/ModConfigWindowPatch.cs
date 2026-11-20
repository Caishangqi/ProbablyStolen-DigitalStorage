using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Composition;
using ProbablyStolen.DigitalStorage.Platform.Integration;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
internal static class ModConfigWindowPatch
{
    private const string TooltipLinkPrefix = "modcfg:";
    private static readonly Dictionary<string, CustomUIWindow> OpenWindows = new(StringComparer.Ordinal);
    private static bool _attached;

    internal static void Attach()
    {
        if (_attached) return;
        IntegrationSettingsView.SwitchStateChanged += OnSwitchStateChanged;
        _attached = true;
    }

    internal static void Detach()
    {
        if (_attached) IntegrationSettingsView.SwitchStateChanged -= OnSwitchStateChanged;
        _attached = false;
        OpenWindows.Clear();
    }

    private static void Postfix(LoadedMod mod, CustomUIWindow __result)
    {
        PinyinModeDropdown.Localize(mod, __result);
        TerminalViewDropdowns.Localize(mod, __result);
        if (mod == null || __result == null) return;

        var modId = mod.Manifest.ID;
        OpenWindows[modId] = __result;
        RefreshIntegrationSwitches(modId, __result);
    }

    private static void OnSwitchStateChanged(string modId, string key, bool locked, string? labelStateKey)
    {
        if (!OpenWindows.TryGetValue(modId, out var window)) return;
        if (window == null || !window.IsAlive)
        {
            OpenWindows.Remove(modId);
            return;
        }

        var config = ModConfig.Find(modId);
        var entry = config?.Entries.FirstOrDefault(candidate => candidate.Key == key);
        if (entry == null) return;
        RefreshEntry(modId, window, entry, locked);
    }

    private static void RefreshIntegrationSwitches(string modId, CustomUIWindow window)
    {
        var config = ModConfig.Find(modId);
        if (config == null) return;
        foreach (var entry in config.Entries)
        {
            if (!IntegrationSettingsView.IsIntegrationSwitch(modId, entry.Key)) continue;
            RefreshEntry(modId, window, entry, IntegrationSettingsView.IsSwitchLocked(modId, entry.Key));
        }
    }

    private static void RefreshEntry(string modId, CustomUIWindow window, ConfigEntry entry, bool locked)
    {
        window.Get(entry.Key)?.SetInteractable(!locked);
        var label = window.Get("label_" + entry.Key);
        if (label == null) return;

        string text;
        try
        {
            text = entry.Label?.Invoke() ?? entry.Key;
        }
        catch
        {
            text = entry.Key;
        }

        if (entry.Tooltip != null)
        {
            var link = TooltipLinkPrefix + modId + ":" + entry.Key;
            text += " <link=\"" + link + "\"><sprite name=\"info\" tint=1></link>";
        }
        label.SetText(text);
    }
}
