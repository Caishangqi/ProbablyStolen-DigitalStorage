using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;

/// <summary>
/// One dock of a terminal window: a machinery panel with the dock's registered title above its buttons, plus the dock's
/// open selection panel. It hangs from the window root outside the frame on its side and ignores the window layout, so
/// it never changes the body height. With no buttons to show, the panel collapses, title included.
/// </summary>
internal sealed class DockView : MonoBehaviour
{
    private readonly List<RaycastResult> _hits = new();
    private readonly Dictionary<string, RectTransform> _buttons = new(StringComparer.Ordinal);
    private CustomUIWindow? _window;
    private ButtonIcons? _icons;
    private ModText? _text;
    private VerticalButtonBar? _bar;
    private Func<TerminalContext>? _context;
    private Action? _changed;
    private RectTransform? _rail;
    private FlyoutView? _flyout;
    private bool _dirty = true;

    /// <summary>
    /// Adds the dock of <paramref name="bar"/> to <paramref name="window"/>, outside its frame on the bar's side with the
    /// top edges aligned. <paramref name="changed"/> runs after a press, so every dock of the window rebuilds.
    /// </summary>
    internal static DockView Create(CustomUIWindow window, ModText text, ButtonIcons icons, VerticalButtonBar bar,
        Func<TerminalContext> context, Action changed)
    {
        var left = bar.Side == DockSide.Left;
        var dock = new GameObject(left ? "dock_left" : "dock_right", typeof(RectTransform)).GetComponent<RectTransform>();
        dock.SetParent(window.Rect, false);
        dock.anchorMin = dock.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        dock.pivot = new Vector2(left ? 1f : 0f, 1f);
        dock.anchoredPosition = new Vector2(left ? -TerminalWindowMetrics.DockGap : TerminalWindowMetrics.DockGap, 0f);
        dock.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var layout = dock.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = dock.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        NativeWindowLayer.AttachInputShield(dock, left ? "dock_input_shield" : "dock_right_input_shield");

        var rail = new GameObject("rail", typeof(RectTransform)).GetComponent<RectTransform>();
        rail.SetParent(dock, false);
        var railLayout = rail.gameObject.AddComponent<VerticalLayoutGroup>();
        railLayout.childControlWidth = railLayout.childControlHeight = true;
        railLayout.childForceExpandWidth = railLayout.childForceExpandHeight = false;

        var view = dock.gameObject.AddComponent<DockView>();
        view._window = window;
        view._icons = icons;
        view._text = text;
        view._bar = bar;
        view._context = context;
        view._changed = changed ?? throw new ArgumentNullException(nameof(changed));
        view._rail = rail;
        view._flyout = new FlyoutView(window, text, icons, bar.Side);
        return view;
    }

    /// <summary>Asks for the buttons to be rebuilt; done after the current click so a pressed button is not destroyed.</summary>
    internal void MarkDirty() => _dirty = true;

    /// <summary>
    /// Builds the buttons now, so the dock shows in the same frame as the window. Only for a dock none of whose buttons
    /// is being clicked, such as one just created.
    /// </summary>
    internal void RebuildNow()
    {
        _dirty = false;
        Rebuild();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }

    private void Update()
    {
        // A press outside the open panel and its button closes the panel. Only the panel is removed here; the buttons
        // are rebuilt after the release, so the press can still click another dock button.
        if (_bar?.OpenSelection == null || _flyout == null || !Input.GetMouseButtonDown(0) || EventSystem.current == null)
            return;
        _hits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, _hits);
        var top = _hits.Count > 0 ? _hits[0].gameObject.transform : null;
        if (top != null && (_flyout.Contains(top) ||
                _buttons.TryGetValue(_bar.OpenSelection.Id, out var trigger) && top.IsChildOf(trigger)))
            return;
        _bar.CloseSelection();
        _flyout.Hide();
        _changed?.Invoke();
    }

    private void LateUpdate()
    {
        if (!_dirty || Input.GetMouseButton(0)) return;
        _dirty = false;
        Rebuild();
    }

    private void OnDestroy() => _flyout?.Hide();

    private void Rebuild()
    {
        if (_window == null || _rail == null || _bar == null || _context == null || _text == null || _icons == null) return;
        var context = _context();
        var buttons = _bar.GetButtons(context);
        CustomUIBuilder.ClearContainer(_rail);
        _buttons.Clear();
        _rail.gameObject.SetActive(buttons.Count > 0);
        if (buttons.Count == 0)
        {
            _flyout?.Hide();
            return;
        }

        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _rail);
        builder.BeginPanel(TerminalWindowMetrics.DockWidth, -1f, TextAnchor.UpperCenter, TerminalWindowMetrics.DockPadding,
            TerminalWindowMetrics.DockSpacing);
        builder.AddHeader(_text.Get(_bar.TitleKey(context)));
        TerminalControls.SetSize(builder.Last, TerminalWindowMetrics.DockButton, 18f);
        if (builder.Last.text != null)
        {
            builder.Last.text.fontSize = 12f;
            builder.Last.text.alignment = TextAlignmentOptions.Center;
            builder.Last.text.overflowMode = TextOverflowModes.Ellipsis;
        }

        foreach (var state in buttons)
        {
            var id = state.Id;
            var button = TerminalButtonView.Add(builder, _text, _icons, state, () => Press(id));
            _buttons[id] = button.root.GetComponent<RectTransform>();
        }

        builder.End();
        ShowFlyout(context);
    }

    private void ShowFlyout(TerminalContext context)
    {
        if (_flyout == null || _bar == null) return;
        var open = _bar.OpenSelection;
        if (open == null || !_buttons.TryGetValue(open.Id, out var trigger))
        {
            _flyout.Hide();
            return;
        }

        // The trigger's position is known only after the layout has run.
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        _flyout.Show(trigger, _bar.GetOpenEntries(context), Select);
    }

    private void Press(string id)
    {
        if (_bar == null || _context == null) return;
        _bar.Press(_context(), id, false);
        _changed?.Invoke();
    }

    private void Select(string entryId)
    {
        if (_bar == null || _context == null) return;
        _bar.Select(_context(), entryId);
        _changed?.Invoke();
    }
}
