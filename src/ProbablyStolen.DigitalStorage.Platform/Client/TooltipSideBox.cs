using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// An extra box that follows the game's item tooltip, built like that tooltip (<c>OverlayHandler</c> creates a
/// <c>PixelWindow</c> holding an 8-pixel padded <c>RichTextElement</c> under <c>overlayRoot</c>),
/// so it looks native. It sits below the tooltip, leaving the right side to mods such as ItemDetail, and moves above
/// the tooltip when there is no room below. Call <see cref="Show"/> every frame the tooltip is shown and
/// <see cref="Hide"/> when it goes away.
/// </summary>
internal sealed class TooltipSideBox
{
    private const int Gap = 2;
    private const int Padding = 8;
    private PixelWindow? _window;
    private RichTextElement? _element;
    private RichText? _shown;

    /// <summary>Shows <paramref name="text"/> next to the visible item tooltip; hides itself when there is none.</summary>
    internal void Show(RichText text, int maxCharWidth = 60)
    {
        var tooltip = OverlayHandler.current?.GetTooltipWindow();
        if (text == null || tooltip?.handler == null || !tooltip.IsVisible() || !EnsureWindow())
        {
            Hide();
            return;
        }

        var window = _window!;
        if (_shown == null || !text.QuickEquals(_shown))
        {
            _element!.SetRichText(text, maxCharWidth);
            window.Validate();
            _shown = text;
        }

        // Both windows share overlayRoot: anchored positions are the top-left corner, with y growing downward as a
        // negative number.
        window.Show(tooltip.rectTransform.position);
        var anchor = tooltip.rectTransform.anchoredPosition;
        var below = new Vector2(anchor.x, anchor.y - tooltip.heightPixels - Gap);
        var bottom = BoundsHeight(window);
        window.rectTransform.anchoredPosition = bottom > 0f && -below.y + window.heightPixels > bottom
            ? new Vector2(anchor.x, Mathf.Min(0f, anchor.y + window.heightPixels + Gap))
            : below;
        window.FixBounds();
    }

    /// <summary>Hides the box; it is shown again by the next <see cref="Show"/>.</summary>
    internal void Hide()
    {
        if (_window?.handler != null && _window.IsVisible()) _window.Hide();
    }

    private bool EnsureWindow()
    {
        if (_window?.handler != null) return true;
        var root = RenderHandler.current?.overlayRoot;
        if (root == null) return false;
        // The overlay belongs to the loaded scene; a new scene needs a new window.
        _window = new PixelWindow();
        _window.handler.SetParent(root);
        _window.handler.name = "Digital Storage Tooltip Box";
        _element = new RichTextElement(canResize: false, -1, -1, hasRaycast: false);
        _element.SetPadding(Padding, Padding, Padding, Padding);
        _window.Attach(_element);
        _shown = null;
        return true;
    }

    /// <summary>Gets the overlay height in the windows' local units, as PixelWindow.FixBounds measures it.</summary>
    private static float BoundsHeight(PixelWindow window)
    {
        var canvas = window.handler.GetComponentInParent<Canvas>();
        if (canvas == null || window.rectTransform.parent == null) return 0f;
        var corner = canvas.pixelRect.max;
        corner.y = canvas.pixelRect.min.y;
        return -window.rectTransform.parent.InverseTransformPoint(corner).y;
    }
}
