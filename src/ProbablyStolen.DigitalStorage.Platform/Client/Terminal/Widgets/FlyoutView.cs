using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;

/// <summary>
/// The selection panel of an open selection button: up to three buttons per column, opening outward from its dock with
/// the top edges aligned and flipping toward the window when the viewport has no room (<see cref="SelectionPanelLayout"/>).
/// It sits under the window root outside the frame, with its own input shield.
/// </summary>
internal sealed class FlyoutView
{
    private const int Padding = TerminalWindowMetrics.DockPadding;
    private const int PanelGap = TerminalWindowMetrics.DockGap;
    private readonly CustomUIWindow _window;
    private readonly ButtonIcons _icons;
    private readonly ModText _text;
    private readonly DockSide _side;
    private readonly SlotMetrics _slots;
    private RectTransform? _root;

    internal FlyoutView(CustomUIWindow window, ModText text, ButtonIcons icons, SlotMetrics slots, DockSide side)
    {
        _side = side;
        _slots = slots ?? throw new ArgumentNullException(nameof(slots));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>Whether <paramref name="target"/> lies in the shown panel.</summary>
    internal bool Contains(Transform target) => _root != null && target.IsChildOf(_root);

    /// <summary>Shows <paramref name="entries"/> next to <paramref name="trigger"/>, replacing any shown panel.</summary>
    internal void Show(RectTransform trigger, IReadOnlyList<ButtonState> entries, Action<string> select)
    {
        Hide();
        if (entries.Count == 0) return;
        var windowRoot = _window.Rect;
        var side = TerminalWindowMetrics.GetDockButton(_slots);
        var geometry = SelectionPanelLayout.Compute(entries.Count, side, side, TerminalWindowMetrics.DockSpacing, Padding);

        var corners = new Vector3[4];
        trigger.GetWorldCorners(corners);
        var topLeft = windowRoot.InverseTransformPoint(corners[1]);
        var topRight = windowRoot.InverseTransformPoint(corners[2]);
        // Panel coordinates grow downward; the window's local y grows upward.
        var (viewportLeft, viewportRight) = ViewportEdges(windowRoot);
        var placement = SelectionPanelLayout.Place(geometry, _side, topLeft.x, -topLeft.y, topRight.x - topLeft.x,
            viewportLeft, viewportRight, PanelGap);

        _root = new GameObject("terminal_flyout", typeof(RectTransform)).GetComponent<RectTransform>();
        _root.SetParent(windowRoot, false);
        _root.SetAsLastSibling();
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0f, 1f);
        _root.sizeDelta = new Vector2(geometry.Width, geometry.Height);
        _root.localPosition = new Vector3(placement.X, -placement.Y, 0f);
        _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        NativeWindowLayer.AttachInputShield(_root, "terminal_flyout_input_shield");

        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _root);
        builder.BeginPanel(geometry.Width, geometry.Height, TextAnchor.UpperLeft, 0f, 0f);
        var panel = builder.CurrentContainer;
        StretchToParent(panel);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var button = TerminalButtonView.Add(builder, _text, _icons, _slots, entry, () => select(entry.Id));
            var rect = button.root.GetComponent<RectTransform>();
            button.root.GetComponent<LayoutElement>().ignoreLayout = true;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(side, side);
            rect.anchoredPosition = new Vector2(geometry.ButtonX(index), -geometry.ButtonY(index));
        }

        builder.End();
    }

    /// <summary>Removes the panel.</summary>
    internal void Hide()
    {
        if (_root != null)
        {
            _root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_root.gameObject);
        }

        _root = null;
    }

    /// <summary>The viewport's side edges in window coordinates; without a viewport the panel never flips.</summary>
    private static (float Left, float Right) ViewportEdges(RectTransform windowRoot)
    {
        if (windowRoot.parent is not RectTransform viewport) return (float.MinValue, float.MaxValue);
        var corners = new Vector3[4];
        viewport.GetWorldCorners(corners);
        return (windowRoot.InverseTransformPoint(corners[0]).x, windowRoot.InverseTransformPoint(corners[2]).x);
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var layout = rect.GetComponent<LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
    }
}
