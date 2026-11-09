using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Widens a machinery window so its title fits instead of being cut with an ellipsis. The native window is exactly as
/// wide as its panel row, and centres the title between two equal margins that hold the close button and any header
/// buttons (<c>PixelWindow.MachineryValidate</c>, Steam build 26525552). Call after the title is set, for example from
/// the window's <c>openCallback</c>, which runs after <c>OpenContentAction</c> has set it.
/// </summary>
internal static class MachineryTitleFit
{
    // Close button: 2 pixels of edge, 16 of button and 2 of gap.
    private const int CloseButtonMargin = 20;
    private const int HeaderButtonGap = 2;
    // A long player label would otherwise make a very wide window; beyond this the native ellipsis applies.
    private const int MaximumWidth = 260;
    private static TreeNodeRender? _probe;

    /// <summary>Fits the title, reserving room for header buttons of the given widths.</summary>
    internal static void Fit(PixelWindow window, params int[] headerButtonWidths)
    {
        if (window?.childElement is not MachineryPanelRow row || string.IsNullOrEmpty(window.titleString)) return;
        var panel = row.Panels.LastOrDefault(candidate => !candidate.isHidden);
        if (panel == null) return;

        // Start from the natural width: an earlier fit is kept as the panel's minimum and requested width.
        panel.SetMinWidth(0);
        panel.ResizePixels(0, 0);
        row.Validate();

        var margin = CloseButtonMargin + headerButtonWidths.Sum(width => width + HeaderButtonGap);
        var needed = Math.Min(MaximumWidth, TitleWidth(window, window.titleString) + 2 * margin);
        var missing = needed - row.widthPixels;
        if (missing <= 0) return;
        panel.SetMinWidth(panel.widthPixels + missing);
        row.Validate();
    }

    /// <summary>Measures the title in the native title style: 10 points, bold machinery font, upper case.</summary>
    private static int TitleWidth(PixelWindow window, string title)
    {
        if (_probe == null)
        {
            _probe = TreeNodeRender.InstantiateText();
            _probe.gameObject.name = "Digital Storage Title Probe";
            MachineryStyle.InitText(_probe, 10, MachineryStyle.TITLE_TEXT, HorizontalAlignmentOptions.Center, uppercase: true);
            _probe.text.color = Color.clear;
        }

        // TextMeshPro only measures an active text, so the probe rides along invisibly under the window.
        _probe.SetParent(window.handler);
        _probe.text.text = title;
        return MachineryStyle.TextWidth(_probe);
    }
}
