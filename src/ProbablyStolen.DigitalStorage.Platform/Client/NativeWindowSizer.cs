using UnityEngine;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Fits and positions a window on the native window layer inside the actual game viewport, and removes its viewport
/// with it; attached by <see cref="NativeWindowLayer.Attach"/>.
/// </summary>
internal sealed class NativeWindowSizer : MonoBehaviour
{
    private TerminalDiagnostics? _diagnostics;
    private RectTransform? _viewport;
    private int _width = -1;
    private int _height = -1;
    private int _x = -1;
    private int _y = -1;
    private float _canvasScale = -1;

    internal void Initialize(RectTransform viewport, TerminalDiagnostics? diagnostics)
    {
        _viewport = viewport;
        _diagnostics = diagnostics;
        Apply();
    }

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (_viewport == null || _viewport.parent is not RectTransform parent || transform is not RectTransform window)
        {
            return;
        }

        var canvas = GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var canvasScale = canvas == null ? 1f : canvas.scaleFactor;
        var width = LetterboxManager.GameAreaWidth;
        var height = LetterboxManager.GameAreaHeight;
        var x = LetterboxManager.GameAreaX;
        var y = LetterboxManager.GameAreaY;
        if (width <= 0 || height <= 0 ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(x, y), camera, out var minimum) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(x + width, y + height), camera, out var maximum))
        {
            return;
        }

        // Native window roots need not fill the screen or use a centered origin.
        _viewport.localPosition = new Vector3((minimum.x + maximum.x) / 2f, (minimum.y + maximum.y) / 2f, 0f);
        _viewport.sizeDelta = new Vector2(Mathf.Abs(maximum.x - minimum.x), Mathf.Abs(maximum.y - minimum.y));
        // The window keeps its design size; only a window larger than the viewport shrinks.
        var designWidth = Mathf.RoundToInt(window.rect.width);
        var designHeight = Mathf.RoundToInt(window.rect.height);
        var scale = TerminalWindowMetrics.GetScale(width, height, canvasScale, designWidth, designHeight);
        window.localScale = new Vector3(scale, scale, 1f);
        var position = window.anchoredPosition;
        window.anchoredPosition = new Vector2(
            TerminalWindowMetrics.GetClampedOffset(position.x, _viewport.rect.width, window.rect.width * scale),
            TerminalWindowMetrics.GetClampedOffset(position.y, _viewport.rect.height, window.rect.height * scale));

        if (_width == width && _height == height && _x == x && _y == y && _canvasScale == canvasScale)
        {
            return;
        }

        _width = width;
        _height = height;
        _x = x;
        _y = y;
        _canvasScale = canvasScale;
        _diagnostics?.Record("window_size",
            $"design={designWidth}x{designHeight} local_scale={scale:0.###} canvas_scale={canvasScale:0.###} viewport={width}x{height}");
        _diagnostics?.Record("window_position",
            $"viewport_origin={x},{y} viewport_local={_viewport.rect.width:0.##}x{_viewport.rect.height:0.##} offset={window.anchoredPosition.x:0.##},{window.anchoredPosition.y:0.##} clamped=true");
    }

    private void OnDestroy()
    {
        if (_viewport != null)
        {
            Destroy(_viewport.gameObject);
        }
    }
}
