using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>The background of one slot.</summary>
internal enum SlotTone
{
    /// <summary>An ordinary slot.</summary>
    Normal = 0,

    /// <summary>The selected rule.</summary>
    Selected = 1,

    /// <summary>Where the dragged item or rule would land.</summary>
    Target = 2,

    /// <summary>The slot a rule is being dragged from.</summary>
    Source = 3
}

/// <summary>
/// A grid of item slots on a <see cref="CustomUIBuilder"/>: a border panel per slot holding a button, the mirrored
/// native item layers, a fallback sprite and an optional quantity badge. The terminal shows badges;
/// partition rules do not.
/// </summary>
internal sealed class ItemSlotGrid
{
    private readonly ItemPictureCapture _pictures;
    private readonly bool _showBadge;
    private readonly float _cellPixels;
    private CustomUIElement?[] _buttons = Array.Empty<CustomUIElement?>();
    private CustomUIElement?[] _badges = Array.Empty<CustomUIElement?>();
    private Image?[] _icons = Array.Empty<Image?>();
    private RectTransform?[] _views = Array.Empty<RectTransform?>();
    private List<Image>[] _layers = Array.Empty<List<Image>>();

    internal ItemSlotGrid(ItemPictureCapture pictures, bool showBadge, float cellPixels)
    {
        _pictures = pictures ?? throw new ArgumentNullException(nameof(pictures));
        _showBadge = showBadge;
        _cellPixels = cellPixels;
    }

    /// <summary>Gets the number of built slots.</summary>
    internal int Count => _buttons.Length;

    /// <summary>Gets the layout parent of the slot panels, for overlays drawn behind them.</summary>
    internal RectTransform? Root { get; private set; }

    /// <summary>
    /// Adds <paramref name="count"/> slots to the builder's current container; the caller opens and closes the grid.
    /// </summary>
    internal void Build(CustomUIBuilder builder, int count, Action<int> click, Action<int>? secondaryClick = null)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (click == null) throw new ArgumentNullException(nameof(click));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "A slot count is not negative.");
        _buttons = new CustomUIElement?[count];
        _badges = new CustomUIElement?[count];
        _icons = new Image?[count];
        _views = new RectTransform?[count];
        _layers = new List<Image>[count];
        for (var index = 0; index < count; index++)
        {
            var slotIndex = index;
            builder.BeginPanel(_cellPixels, _cellPixels, padding: 2f, spacing: 0f, id: $"slot_{index}");
            if (index == 0) Root = builder.Last.Rect.parent as RectTransform;
            ConfigureBorder(builder.Last);
            builder.AddPointerButton(string.Empty, () => click(slotIndex), $"button_{index}");
            if (secondaryClick != null)
                builder.Last.root.AddComponent<TerminalSlotClick>().Initialize(() => secondaryClick(slotIndex));
            _buttons[index] = builder.Last;
            _icons[index] = ConfigureButton(builder.Last);
            _views[index] = CreateItemView(builder.Last);
            _layers[index] = new List<Image>();
            if (_showBadge)
            {
                builder.AddLabel(string.Empty, $"badge_{index}");
                ConfigureBadge(builder.Last);
                _badges[index] = builder.Last;
            }

            builder.End();
        }
    }

    /// <summary>Gets a slot's button, or null outside the grid.</summary>
    internal CustomUIElement? Button(int index) => index >= 0 && index < _buttons.Length ? _buttons[index] : null;

    /// <summary>Shows an unoccupied slot: no picture and no badge; clickable only when it can be a target.</summary>
    internal void SetEmpty(int index, bool interactable = false)
    {
        var button = Button(index);
        button?.SetVisible(true);
        TerminalTooltipPresenter.SetText(button, string.Empty);
        if (button?.button != null) button.button.interactable = interactable;
        ShowPicture(index, null, occupied: false);
        Badge(index)?.SetVisible(false);
    }

    /// <summary>Shows an occupied slot. A null badge text hides the badge.</summary>
    internal void SetEntry(int index, ItemPicture? picture, bool showPicture, bool interactable, string? badgeText)
    {
        var button = Button(index);
        button?.SetVisible(true).SetPreferredSize(_cellPixels - 4f, _cellPixels - 4f);
        TerminalTooltipPresenter.SetText(button, string.Empty);
        if (button?.button != null) button.button.interactable = interactable;
        ShowPicture(index, picture, showPicture);
        var badge = Badge(index);
        if (badgeText == null) badge?.SetVisible(false);
        else badge?.SetVisible(true).SetText(badgeText);
    }

    /// <summary>Renders a cached picture, or clears the slot when it is unoccupied.</summary>
    internal void ShowPicture(int index, ItemPicture? picture, bool occupied)
    {
        if (index < 0 || index >= _buttons.Length) return;
        _pictures.Show(_views[index], _layers[index], _icons[index], picture, occupied);
    }

    /// <summary>Mirrors a live sample into a slot, hiding the fallback icon when it succeeds.</summary>
    internal bool MirrorLive(int index, GameItemElement sample)
    {
        if (index < 0 || index >= _views.Length || _views[index] == null) return false;
        var mirrored = NativeItemView.Sync(_views[index]!, _layers[index], sample);
        if (mirrored && _icons[index] != null) _icons[index]!.enabled = false;
        return mirrored;
    }

    /// <summary>Sets a slot's background.</summary>
    internal void SetTone(int index, SlotTone tone)
    {
        var button = Button(index);
        if (button?.image == null) return;
        button.image.color = tone switch
        {
            SlotTone.Selected => new Color32(92, 78, 120, 255),
            SlotTone.Target => new Color32(62, 118, 74, 255),
            SlotTone.Source => new Color32(22, 20, 28, 255),
            _ => new Color32(35, 32, 45, 255)
        };
    }

    /// <summary>
    /// Adds the native range highlight behind the slots, sized to the grid, and returns it hidden.
    /// A gutter of two units keeps the native border visible behind opaque slot panels.
    /// </summary>
    internal Image? CreateDropHighlight(float gridWidth, float gridHeight)
    {
        if (Root == null) return null;
        var root = new GameObject("terminal_drop_range", typeof(RectTransform));
        root.transform.SetParent(Root, false);
        root.transform.SetAsFirstSibling();
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(-2f, 2f);
        rect.sizeDelta = new Vector2(gridWidth + 4f, gridHeight + 4f);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        var highlight = root.AddComponent<Image>();
        highlight.type = Image.Type.Sliced;
        highlight.raycastTarget = false;
        highlight.enabled = false;
        return highlight;
    }

    /// <summary>Shows a range highlight in the native colours: green on hover, red when refused, blue otherwise.</summary>
    internal static void SetDropHighlight(Image? highlight, TerminalDropHighlight state)
    {
        if (highlight == null) return;
        if (state == TerminalDropHighlight.Hidden || RenderHandler.current == null)
        {
            highlight.enabled = false;
            return;
        }

        var color = state == TerminalDropHighlight.Hover ? RenderHandler.BackgroundColor.Green :
            state == TerminalDropHighlight.Rejected ? RenderHandler.BackgroundColor.Red : RenderHandler.BackgroundColor.Blue;
        RenderHandler.current.SetHighlightColor(highlight, color, RenderHandler.BackgroundType.Border);
    }

    /// <summary>
    /// Removes the press tint of every slot, for a grid that reads the pointer itself and shows its state through
    /// <see cref="SetTone"/>.
    /// </summary>
    internal void DisableClickTint()
    {
        foreach (var button in _buttons)
            if (button?.button != null) button.button.transition = Selectable.Transition.None;
    }

    /// <summary>Sets a slot's hover text.</summary>
    internal void SetTooltip(int index, string text) => TerminalTooltipPresenter.SetText(Button(index), text);

    /// <summary>Gets the slot under a screen point among the first <paramref name="limit"/> slots, or -1.</summary>
    internal int IndexAt(Vector2 screenPoint, Camera? camera, int limit)
    {
        for (var index = 0; index < _buttons.Length && index < limit; index++)
        {
            var button = _buttons[index];
            if (button != null && RectTransformUtility.RectangleContainsScreenPoint(button.Rect, screenPoint, camera))
                return index;
        }

        return -1;
    }

    /// <summary>Hides every slot's custom tooltip, for a hint that replaces them.</summary>
    internal void HideTooltips()
    {
        foreach (var button in _buttons)
        {
            var trigger = button?.root.GetComponent<CustomUITooltipTrigger>();
            if (trigger != null) CustomUIManager.Instance?.HideTooltip(trigger);
        }
    }

    /// <summary>Drops references to destroyed controls after the window closes.</summary>
    internal void Reset()
    {
        _buttons = Array.Empty<CustomUIElement?>();
        _badges = Array.Empty<CustomUIElement?>();
        _icons = Array.Empty<Image?>();
        _views = Array.Empty<RectTransform?>();
        _layers = Array.Empty<List<Image>>();
        Root = null;
    }

    private CustomUIElement? Badge(int index) => index >= 0 && index < _badges.Length ? _badges[index] : null;

    private static void ConfigureBorder(CustomUIElement slot)
    {
        slot.image.sprite = null;
        slot.image.color = new Color32(57, 51, 65, 255);
        slot.image.raycastTarget = false;
    }

    private static Image ConfigureButton(CustomUIElement button)
    {
        button.image.sprite = null;
        button.image.color = new Color32(35, 32, 45, 255);
        button.button.transition = Selectable.Transition.ColorTint;
        var icon = new GameObject("terminal_icon", typeof(RectTransform)).AddComponent<Image>();
        icon.transform.SetParent(button.root.transform, false);
        var rect = (RectTransform)icon.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -6f);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return icon;
    }

    /// <summary>Creates the container that holds the mirrored native item layers, over the icon area.</summary>
    private static RectTransform CreateItemView(CustomUIElement button)
    {
        var view = (RectTransform)new GameObject("terminal_item_view", typeof(RectTransform)).transform;
        view.SetParent(button.root.transform, false);
        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.offsetMin = new Vector2(6f, 6f);
        view.offsetMax = new Vector2(-6f, -6f);
        view.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return view;
    }

    private static void ConfigureBadge(CustomUIElement badge)
    {
        var layout = badge.root.GetComponent<LayoutElement>() ?? badge.root.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        badge.Rect.anchorMin = Vector2.zero;
        badge.Rect.anchorMax = Vector2.one;
        badge.Rect.offsetMin = new Vector2(3f, 2f);
        badge.Rect.offsetMax = new Vector2(-3f, -2f);
        if (badge.text != null)
        {
            badge.text.alignment = TextAlignmentOptions.BottomRight;
            badge.text.fontSize = 12f;
            badge.text.raycastTarget = false;
        }
    }
}
