using UnityEngine;
using UnityEngine.UI;

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
    private Image? _border;
    private Sprite? _nativeBorder;
    private Sprite? _frame;
    private bool _borderMissing;
    private Image.Type _nativeType = Image.Type.Tiled;
    private float _nativeMultiplier = 1f;

    /// <summary>
    /// Shows <paramref name="text"/> below the visible item tooltip, or below <paramref name="after"/> when that box is
    /// showing; hides itself when there is no item tooltip.
    /// </summary>
    internal void Show(RichText text, int maxCharWidth = 60, TooltipSideBox? after = null)
    {
        ShowFramed(text, null, Color.white, maxCharWidth, after);
    }

    /// <summary>
    /// Shows the box like <see cref="Show"/>, with its border drawn by <paramref name="frame"/> tinted with
    /// <paramref name="tint"/>; a null frame keeps the game's own border.
    /// </summary>
    internal void ShowFramed(RichText text, Sprite? frame, Color tint, int maxCharWidth = 60, TooltipSideBox? after = null)
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

        // Validate resets the border image to white, so the frame is applied after it on every show.
        ApplyBorder(frame, tint);

        // Both windows share overlayRoot: anchored positions are the top-left corner, with y growing downward as a
        // negative number.
        var previous = after?._window;
        var host = previous?.handler != null && previous.IsVisible() ? previous : tooltip;
        window.Show(host.rectTransform.position);
        var anchor = host.rectTransform.anchoredPosition;
        var below = new Vector2(anchor.x, anchor.y - host.heightPixels - Gap);
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

    private void ApplyBorder(Sprite? frame, Color tint)
    {
        var border = FindBorder();
        if (border == null) return;
        if (frame == null)
        {
            if (_frame != null)
            {
                border.sprite = _nativeBorder;
                border.type = _nativeType;
                border.pixelsPerUnitMultiplier = _nativeMultiplier;
                border.color = Color.white;
            }

            _frame = null;
            return;
        }

        if (!ReferenceEquals(_frame, frame) || border.sprite != frame)
        {
            border.sprite = frame;
            border.type = Image.Type.Sliced;
            border.fillCenter = false;
            border.pixelsPerUnitMultiplier = 1f;
            border.SetAllDirty();
            _frame = frame;
        }

        if (border.color != tint) border.color = tint;
    }

    /// <summary>Finds the window's own border image, the one drawn with the game's outside sprite.</summary>
    private Image? FindBorder()
    {
        if (_border != null) return _border;
        if (_borderMissing || _window?.handler == null) return null;
        var outside = RenderHandler.current?.outside;
        foreach (var image in _window.handler.GetComponentsInChildren<Image>(true))
        {
            if (outside == null || image.sprite != outside) continue;
            _border = image;
            _nativeBorder = outside;
            _nativeType = image.type;
            _nativeMultiplier = image.pixelsPerUnitMultiplier;
            return image;
        }

        _borderMissing = true;
        Debug.LogWarning("Digital Storage: tooltip box border not found; the game's border stays.");
        return null;
    }

    private bool EnsureWindow()
    {
        if (_window?.handler != null) return true;
        var root = RenderHandler.current?.overlayRoot;
        if (root == null) return false;
        // The overlay belongs to the loaded scene; a new scene needs a new window.
        _border = null;
        _frame = null;
        _borderMissing = false;
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
