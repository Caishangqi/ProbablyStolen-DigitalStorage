using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Stacking;

/// <summary>Names and shared styling of the stacking pages.</summary>
internal static class StackingPageText
{
    internal static string FieldName(ModText text, string fieldId)
    {
        var field = StackingFieldCatalog.Find(fieldId);
        if (field == null) return fieldId;
        if (field.TagKey != null) return field.TagKey;
        if (field.FeatureId != null) return text.Get(field.NameKey, field.FeatureId);
        return text.Get(field.NameKey);
    }

    internal static string Description(ModText text, StackingField field)
    {
        if (field.TagKey != null) return string.Empty;
        return field.FeatureId != null ? text.Get(field.DescriptionKey, field.FeatureId) : text.Get(field.DescriptionKey);
    }

    /// <summary>The light yellow behind an item rule row that overrides a global row; it marks a choice, not an error.</summary>
    internal static readonly Color OverrideFill = new(1f, 0.85f, 0.35f, 0.18f);

    /// <summary>The light yellow of an item name whose rule overrides global rows.</summary>
    internal static readonly Color OverrideText = new(1f, 0.86f, 0.5f, 1f);

    /// <summary>Describes how an item rule row relates to the global rows, or null when no switched-on row names its field.</summary>
    internal static string? GlobalRelation(ModText text, StackingConfig config, StackingRule row)
    {
        if (config.OverriddenGlobal(row) is { } global)
            return text.Get("stacking_rule_overrides_global", text.Get(ModeKey(global.Mode)), text.Get(ModeKey(row.Mode)));
        return config.RepeatsGlobal(row) ? text.Get("stacking_rule_same_as_global") : null;
    }

    internal static string ModeKey(StackingMode mode) =>
        mode == StackingMode.Ignore ? "stacking_mode_ignore" : "stacking_mode_match";

    internal static void Muted(CustomUIElement label)
    {
        if (label.text != null) label.text.color = MachineryStyle.BUTTON_TEXT_DISABLED;
    }

    internal static Color Tint(StackingMarker marker) =>
        RenderHandler.GetColorFromPalette((RenderHandler.ColorPalette)marker.Rgb);

    internal static Camera? EventCamera(TerminalWindowHost window)
    {
        var canvas = window.CustomWindow.Rect.GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    /// <summary>Reads the stored identities, or none when the inventory cannot be read.</summary>
    internal static IReadOnlyList<ItemKey> StoredKeys(TerminalWindowHost window, StorageTerminalServices storage)
    {
        try
        {
            return storage.ReadSnapshot().Entries.Select(entry => entry.Key).ToArray();
        }
        catch (Exception error)
        {
            window.WriteDiagnostic($"stacking_page failed=read_inventory error={error.GetType().Name}");
            return Array.Empty<ItemKey>();
        }
    }
}

/// <summary>
/// Display names of item definitions for the stacking pages: the item's own name read from a stored sample without a
/// custom name, else the stored entry's name, else the identifier. Rules of items no longer stored show the identifier.
/// </summary>
internal sealed class StackingItemNames
{
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    internal StackingItemNames(TerminalWindowHost window, StorageTerminalServices storage)
    {
        try
        {
            foreach (var entry in storage.ReadSnapshot().Entries)
            {
                var id = entry.Key.Identifier;
                if (_names.ContainsKey(id)) continue;
                var name = storage.FindSample(entry.Key)?.GetDisplayName(preferCustomName: false);
                _names[id] = !string.IsNullOrEmpty(name) ? name! : !string.IsNullOrEmpty(entry.DisplayName) ? entry.DisplayName : id;
            }
        }
        catch (Exception error)
        {
            window.WriteDiagnostic($"stacking_page failed=read_names error={error.GetType().Name}");
        }
    }

    /// <summary>Gets whether a stored item has this identifier.</summary>
    internal bool IsStored(string itemId) => _names.ContainsKey(itemId);

    /// <summary>Gets the display name, or the identifier when no stored item has it.</summary>
    internal string Of(string itemId) => _names.TryGetValue(itemId, out var name) ? name : itemId;
}
