using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;

/// <summary>
/// The rule card look shared by the partition and stacking pages: a lightly filled row per rule with fixed-size
/// controls, so long values never widen a card.
/// </summary>
internal static class RuleCardWidgets
{
    /// <summary>The height of one control in a card.</summary>
    internal const float ControlHeight = 28f;

    /// <summary>The inner padding of a card.</summary>
    internal const float CardPadding = 6f;

    // Room kept beside the cards when the scroll view's scrollbar width cannot be read.
    private const float ScrollGutter = 16f;

    /// <summary>The fill of a card.</summary>
    internal static readonly Color CardFill = new(1f, 1f, 1f, 0.06f);

    /// <summary>The fill of an alternate table row.</summary>
    internal static readonly Color StripeFill = new(1f, 1f, 1f, 0.04f);

    /// <summary>The inner padding of a rule list box.</summary>
    internal const float ListPadding = 4f;

    /// <summary>
    /// Opens the light box that holds a rule list, shown even when empty so players see where rules will appear;
    /// the caller closes it.
    /// </summary>
    internal static void BeginListBox(CustomUIBuilder builder, float gap, float width, Color? fill = null)
    {
        builder.BeginColumn(gap);
        var box = builder.CurrentContainer.gameObject;
        Fill(box, fill ?? StripeFill);
        if (box.GetComponent<VerticalLayoutGroup>() is { } column)
            column.padding = new RectOffset((int)ListPadding, (int)ListPadding, (int)ListPadding, (int)ListPadding);
        FixWidth(box, width);
        var layout = box.GetComponent<LayoutElement>()!;
        layout.minHeight = ControlHeight + 2 * CardPadding + 2 * ListPadding;
    }

    /// <summary>Opens the framed box that states a rule's final meaning below its editor; the caller closes it.</summary>
    internal static RectTransform BeginSpecBox(CustomUIBuilder builder)
    {
        builder.BeginColumn(2f);
        var box = builder.CurrentContainer;
        var image = box.gameObject.AddComponent<Image>();
        image.sprite = MachineryStyle.Intake;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 0.5f;
        image.raycastTarget = false;
        if (box.GetComponent<VerticalLayoutGroup>() is { } column) column.padding = new RectOffset(10, 10, 8, 8);
        return box;
    }

    /// <summary>Opens a card row of a fixed width; the caller closes it.</summary>
    internal static void BeginCard(CustomUIBuilder builder, float gap, float width)
    {
        builder.BeginRow(gap, TextAnchor.MiddleLeft);
        Fill(builder.CurrentContainer.gameObject, CardFill);
        if (builder.CurrentContainer.GetComponent<HorizontalLayoutGroup>() is { } card)
            card.padding = new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding);
        FixWidth(builder.CurrentContainer.gameObject, width);
    }

    /// <summary>Cuts a label with an ellipsis instead of wrapping it.</summary>
    internal static void Ellipsis(CustomUIElement label)
    {
        if (label.text == null) return;
        label.text.overflowMode = TextOverflowModes.Ellipsis;
        label.text.enableWordWrapping = false;
    }

    /// <summary>Reserves a separate gutter for the native scroll bar and lets the list box provide the fill.</summary>
    internal static float ConfigureListScroll(GameObject view, float width, float gap)
    {
        FixWidth(view, width);
        if (view.GetComponentInChildren<ScrollRect>(true) is not { } scroll || scroll.viewport == null || scroll.content == null)
            return width;

        var gutter = ScrollbarSpace(view) + 8f;
        if (view != scroll.viewport.gameObject && view.GetComponent<Image>() is { } background)
        {
            var color = background.color;
            color.a = 0;
            background.color = color;
        }
        scroll.horizontal = false;
        if (scroll.verticalScrollbar != null)
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.viewport.anchorMin = Vector2.zero;
        scroll.viewport.anchorMax = Vector2.one;
        scroll.viewport.offsetMin = Vector2.zero;
        scroll.viewport.offsetMax = new Vector2(-gutter, 0);
        // The viewport Image still provides the mask geometry and the wheel hit area.
        if (scroll.viewport.GetComponent<Mask>() is { } mask) mask.showMaskGraphic = false;
        scroll.content.anchorMin = new Vector2(0, 1);
        scroll.content.anchorMax = Vector2.one;
        scroll.content.pivot = new Vector2(0, 1);
        scroll.content.anchoredPosition = Vector2.zero;
        scroll.content.sizeDelta = new Vector2(0, scroll.content.sizeDelta.y);
        if (scroll.content.GetComponent<VerticalLayoutGroup>() is { } column)
        {
            column.padding = new RectOffset();
            column.spacing = gap;
        }

        return width - gutter;
    }

    /// <summary>Gets the width a scroll view's vertical scrollbar takes beside its content.</summary>
    internal static float ScrollbarSpace(GameObject view)
    {
        var scroll = view.GetComponentInChildren<ScrollRect>(true);
        var bar = scroll == null ? null : scroll.verticalScrollbar;
        var width = bar == null ? 0f : ((RectTransform)bar.transform).rect.width;
        return width > 0f ? width + Math.Max(0f, scroll!.verticalScrollbarSpacing) : ScrollGutter;
    }

    /// <summary>Fixes an object's layout width.</summary>
    internal static void FixWidth(GameObject target, float width)
    {
        var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        layout.layoutPriority = 2;
        layout.minWidth = layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
    }

    /// <summary>Fills an object with a flat color that does not catch the pointer.</summary>
    internal static void Fill(GameObject target, Color color)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = MachineryStyle.White;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
    }

    // TMP_InputField reports its text size at priority 1; priority 2 keeps long values from widening the card.
    /// <summary>Fixes a control's size.</summary>
    internal static void SetSize(CustomUIElement control, float width, float height)
    {
        var layout = control.root.GetComponent<LayoutElement>() ?? control.root.AddComponent<LayoutElement>();
        layout.layoutPriority = 2;
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleWidth = layout.flexibleHeight = 0f;
    }
}
