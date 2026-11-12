using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>One choice of a <see cref="PopupDropdown"/>; the detail is shown after the label in a muted color.</summary>
internal readonly struct DropdownOption
{
    internal DropdownOption(string label, string? detail = null)
    {
        Label = label;
        Detail = detail;
    }

    internal string Label { get; }

    internal string? Detail { get; }
}

// CustomUIMachineryStyle.Reskin replaces every native dropdown image, including its arrow, with the same plate.
// Keeping the popup on the window root also prevents the list from moving the surrounding controls.
internal sealed class PopupDropdown
{
    private const float RowHeight = 26f;
    private const int VisibleRows = 8;
    private const float Padding = 4f;
    private const float MinimumWidth = 180f;
    // Plain white tinted down to the closed and hovered colors, since a color tint can only darken.
    private static readonly Color FieldHover = new Color32(66, 58, 82, 255);
    private static readonly Color FieldNormalTint = new(0.62f, 0.59f, 0.63f, 1f);
    private static readonly Color RowHover = new(1f, 1f, 1f, 0.12f);
    private static readonly Color Accent = MachineryStyle.TAB_TEXT_HOVER;
    private static readonly string MutedColor = "#" + ColorUtility.ToHtmlStringRGB(MachineryStyle.BUTTON_TEXT_DISABLED);
    private readonly CustomUIWindow _window;
    private readonly Func<string, string, bool> _matches;
    private RectTransform? _popup;
    private RectTransform? _anchor;
    private RectTransform? _list;
    private IReadOnlyList<DropdownOption> _options = Array.Empty<DropdownOption>();
    private int _selected;
    private Action<int>? _choose;
    private string _search = string.Empty;

    internal PopupDropdown(CustomUIWindow window, Func<string, string, bool> matches)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _matches = matches ?? throw new ArgumentNullException(nameof(matches));
    }

    internal bool IsOpen => _popup != null;

    internal bool IsOpenFor(CustomUIElement field) => _popup != null && _anchor == field.Rect;

    internal static CustomUIElement AddField(CustomUIBuilder builder, string value, float width, Action onClick)
    {
        builder.AddButton(value, onClick);
        var field = builder.Last;
        StyleField(field, width);
        return field;
    }

    internal static void StyleField(CustomUIElement field, float width)
    {
        if (field.image != null)
        {
            field.image.sprite = MachineryStyle.PlateNotched;
            field.image.type = Image.Type.Sliced;
            field.image.pixelsPerUnitMultiplier = 0.5f;
            field.image.color = FieldHover;
        }

        if (field.button != null)
        {
            field.button.transition = Selectable.Transition.ColorTint;
            var colors = field.button.colors;
            colors.normalColor = FieldNormalTint;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = FieldNormalTint;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            field.button.colors = colors;
            ShowNormalNow(field.button, colors.normalColor);
        }

        if (field.root.GetComponent<HorizontalLayoutGroup>() is { } layout)
        {
            layout.padding = new RectOffset(8, 22, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        if (field.text != null)
        {
            field.text.color = MachineryStyle.BODY_TEXT;
            field.text.alignment = TextAlignmentOptions.MidlineLeft;
            field.text.overflowMode = TextOverflowModes.Ellipsis;
            field.text.enableWordWrapping = false;
            // The label takes the field's free width instead of its text width, so long values are cut with an ellipsis.
            var label = field.text.GetComponent<LayoutElement>() ?? field.text.gameObject.AddComponent<LayoutElement>();
            label.layoutPriority = 2;
            label.preferredWidth = 0f;
            label.flexibleWidth = 1f;
        }

        Size(field.root, width, 28f);
        var arrow = PixelArrow.Add(field.root.transform, PixelArrow.Direction.Down, MachineryStyle.BODY_TEXT);
        arrow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var rect = arrow.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-8f, 0f);
    }

    internal void Open(CustomUIElement field, IReadOnlyList<DropdownOption> options, int selected, Action<int> choose,
        string? searchPlaceholder = null)
    {
        Close();
        if (!_window.IsAlive || field.root == null) return;
        _anchor = field.Rect;
        _options = options;
        _selected = selected;
        _choose = choose;
        _search = string.Empty;

        var root = new GameObject("digital_storage_dropdown_list", typeof(RectTransform));
        _popup = (RectTransform)root.transform;
        _popup.SetParent(_window.Rect, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        var background = root.AddComponent<Image>();
        background.sprite = MachineryStyle.Intake;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 0.5f;
        background.color = Color.white;
        var column = root.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
        column.spacing = 2f;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _popup);
        var searchHeight = 0f;
        TMP_InputField? searchInput = null;
        if (searchPlaceholder != null)
        {
            builder.AddInput(searchPlaceholder, string.Empty, null, null, typed =>
            {
                _search = typed ?? string.Empty;
                RebuildList();
            });
            Size(builder.Last.root, -1f, 28f);
            searchInput = builder.Last.input;
            searchHeight = 28f + column.spacing;
        }

        var listHeight = Math.Min(Math.Max(options.Count, 1), VisibleRows) * RowHeight;
        builder.BeginScroll(listHeight);
        _list = builder.CurrentContainer;
        builder.End();
        RebuildList();

        var width = Math.Max(_anchor.rect.width, MinimumWidth);
        var height = searchHeight + listHeight + 2 * Padding;
        _popup.sizeDelta = new Vector2(width, height);
        Place(width, height);
        _popup.SetAsLastSibling();
        searchInput?.ActivateInputField();
    }

    internal void Close()
    {
        if (_popup != null) UnityEngine.Object.Destroy(_popup.gameObject);
        _popup = null;
        _anchor = null;
        _list = null;
        _choose = null;
    }

    /// <summary>Closes the list when the player presses outside it and outside its field; call every frame.</summary>
    internal void CloseOnOutsidePress(Camera? camera)
    {
        if (_popup == null || !Input.GetMouseButtonDown(0)) return;
        var mouse = (Vector2)Input.mousePosition;
        if (RectTransformUtility.RectangleContainsScreenPoint(_popup, mouse, camera)) return;
        if (_anchor != null && RectTransformUtility.RectangleContainsScreenPoint(_anchor, mouse, camera)) return;
        Close();
    }

    private void RebuildList()
    {
        if (_list == null || !_window.IsAlive) return;
        CustomUIBuilder.ClearContainer(_list);
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _list);
        for (var index = 0; index < _options.Count; index++)
        {
            var option = _options[index];
            if (_search.Trim().Length > 0 && !_matches(option.Label, _search.Trim())) continue;
            var chosen = index;
            var label = option.Detail is { Length: > 0 } detail
                ? option.Label + "  <color=" + MutedColor + ">" + detail + "</color>"
                : option.Label;
            builder.AddButton(label, () => Choose(chosen));
            StyleRow(builder.Last, index == _selected);
        }
    }

    private void Choose(int index)
    {
        var choose = _choose;
        Close();
        choose?.Invoke(index);
    }

    private static void StyleRow(CustomUIElement row, bool selected)
    {
        if (row.image != null)
        {
            row.image.sprite = MachineryStyle.White;
            row.image.type = Image.Type.Simple;
            row.image.color = RowHover;
        }

        if (row.button != null)
        {
            row.button.transition = Selectable.Transition.ColorTint;
            var colors = row.button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, selected ? 0.5f : 0f);
            colors.highlightedColor = colors.pressedColor = Color.white;
            colors.selectedColor = colors.normalColor;
            row.button.colors = colors;
            ShowNormalNow(row.button, colors.normalColor);
        }

        if (row.root.GetComponent<HorizontalLayoutGroup>() is { } layout)
        {
            layout.padding = new RectOffset(10, 6, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        if (row.text != null)
        {
            row.text.color = selected ? MachineryStyle.VALUE_TEXT : MachineryStyle.BODY_TEXT;
            row.text.alignment = TextAlignmentOptions.MidlineLeft;
            row.text.overflowMode = TextOverflowModes.Ellipsis;
            row.text.enableWordWrapping = false;
            var label = row.text.GetComponent<LayoutElement>() ?? row.text.gameObject.AddComponent<LayoutElement>();
            label.layoutPriority = 2;
            label.preferredWidth = 0f;
            label.flexibleWidth = 1f;
        }

        Size(row.root, -1f, RowHeight - 2f);
        if (!selected) return;
        var bar = new GameObject("digital_storage_dropdown_selected", typeof(RectTransform)).AddComponent<Image>();
        bar.transform.SetParent(row.root.transform, false);
        bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        bar.sprite = MachineryStyle.White;
        bar.color = Accent;
        bar.raycastTarget = false;
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(3f, 0f);
        rect.anchoredPosition = Vector2.zero;
    }

    private void Place(float width, float height)
    {
        if (_popup == null || _anchor == null) return;
        var parent = _window.Rect;
        var corners = new Vector3[4];
        _anchor.GetWorldCorners(corners);
        var bottomLeft = parent.InverseTransformPoint(corners[0]);
        var topLeft = parent.InverseTransformPoint(corners[1]);
        var bounds = parent.rect;
        if (parent.parent is RectTransform viewport)
        {
            viewport.GetWorldCorners(corners);
            var min = parent.InverseTransformPoint(corners[0]);
            var max = parent.InverseTransformPoint(corners[2]);
            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        var below = bottomLeft.y - 2f;
        var above = topLeft.y + 2f + height;
        var roomBelow = below - bounds.yMin;
        var roomAbove = bounds.yMax - (topLeft.y + 2f);
        var top = roomBelow >= height || roomBelow >= roomAbove ? below : above;
        top = Mathf.Clamp(top, bounds.yMin + height, Mathf.Max(bounds.yMin + height, bounds.yMax));
        var left = Mathf.Clamp(bottomLeft.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - width));
        // Anchored at the parent's pivot, so anchored positions equal the parent's local coordinates.
        _popup.anchorMin = _popup.anchorMax = parent.pivot;
        _popup.pivot = new Vector2(0f, 1f);
        _popup.anchoredPosition = new Vector2(left, top);
        _popup.sizeDelta = new Vector2(width, height);
    }

    // Changing Selectable.transition starts a fade from the image color, making rebuilt rows flash on each keystroke.
    private static void ShowNormalNow(Selectable selectable, Color normal) =>
        selectable.targetGraphic?.CrossFadeColor(normal, 0f, true, true);

    private static void Size(GameObject root, float width, float height)
    {
        var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        // Outranks the control's own layout values (TMP_InputField and TMP text report priority 1).
        layout.layoutPriority = 2;
        if (width > 0f)
        {
            layout.minWidth = layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
        }

        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
    }
}
