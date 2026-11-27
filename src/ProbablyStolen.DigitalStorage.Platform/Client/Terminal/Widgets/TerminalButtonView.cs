using System;
using ProbablyStolen.DigitalStorage.Api.Client;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;

internal static class TerminalButtonView
{
    /// <summary>Adds a dock button at the dock button size of <paramref name="slots"/>.</summary>
    internal static CustomUIElement Add(CustomUIBuilder builder, ModText text, ButtonIcons icons, SlotMetrics slots,
        ButtonState state, Action onClick)
    {
        var side = TerminalWindowMetrics.GetDockButton(slots);
        var button = AddIcon(builder, icons, state.Appearance.IconKey, state.Selected, onClick, side, side,
            TerminalWindowMetrics.GetDockIcon(slots));
        var appearance = state.Appearance;
        TerminalTooltipPresenter.SetButtonText(button, appearance.TooltipArgKey == null
            ? text.Get(appearance.TooltipKey)
            : text.Get(appearance.TooltipKey, text.Get(appearance.TooltipArgKey)));
        return button;
    }

    internal static CustomUIElement AddIcon(CustomUIBuilder builder, ButtonIcons icons, string key, bool selected,
        Action onClick, float size) => AddIcon(builder, icons, key, selected, onClick, size, size);

    internal static CustomUIElement AddIcon(CustomUIBuilder builder, ButtonIcons icons, string key, bool selected,
        Action onClick, float width, float height) => AddIcon(builder, icons, key, selected, onClick, width, height, 32f);

    /// <summary>Adds an icon button; the icon is <paramref name="iconSide"/> wide unless the button is too small for it.</summary>
    internal static CustomUIElement AddIcon(CustomUIBuilder builder, ButtonIcons icons, string key, bool selected,
        Action onClick, float width, float height, float iconSide)
    {
        builder.AddPointerButton(string.Empty, onClick, null);
        var element = builder.Last;
        TerminalControls.SetSize(element, width, height);
        if (element.text != null) element.text.gameObject.SetActive(false);
        var image = new GameObject("button_icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(element.root.transform, false);
        image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        image.rectTransform.anchoredPosition = Vector2.zero;
        var iconSize = Mathf.Min(iconSide, Mathf.Min(width, height) - 4f);
        image.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
        image.raycastTarget = false;
        image.sprite = icons.Get(key);
        if (selected) AddSelectionFrame(element.root.transform);
        return element;
    }

    /// <summary>Adds the one-pixel frame that marks a selected button; toggle the returned object to show or hide it.</summary>
    internal static GameObject AddSelectionFrame(Transform parent)
    {
        var frame = new GameObject("selection_frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(parent, false);
        frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        AddBorder(frame, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -1f), Vector2.zero);
        AddBorder(frame, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));
        AddBorder(frame, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(1f, 0f));
        AddBorder(frame, new Vector2(1f, 0f), Vector2.one, new Vector2(-1f, 0f), Vector2.zero);
        return frame.gameObject;
    }

    private static void AddBorder(Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var line = new GameObject("selection_border", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        line.transform.SetParent(parent, false);
        line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        line.raycastTarget = false;
        line.color = MachineryStyle.VALUE_TEXT;
        line.rectTransform.anchorMin = min;
        line.rectTransform.anchorMax = max;
        line.rectTransform.offsetMin = offsetMin;
        line.rectTransform.offsetMax = offsetMax;
    }
}
