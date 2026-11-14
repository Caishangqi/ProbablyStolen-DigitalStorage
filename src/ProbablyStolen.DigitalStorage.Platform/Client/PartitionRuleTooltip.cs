using System;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Displays rule lore using the same window, text element and padding as the native item tooltip.</summary>
internal sealed class PartitionRuleTooltip : IDisposable
{
    private const int Padding = 8;
    private PixelWindow? _window;
    private RichTextElement? _element;
    private RichText? _content;
    private RichText? _rendered;

    internal void SetContent(RichText content) => _content = content;

    /// <summary>Follows the pointer without rebuilding unchanged lore or sharing the game's item-tooltip owner.</summary>
    internal void FollowPointer()
    {
        if (_content == null || !EnsureWindow()) return;
        if (!ReferenceEquals(_rendered, _content))
        {
            _element!.SetRichText(_content, ItemTooltipHandler.BASE_TOOLTIP_CHAR_WIDTH);
            _window!.Validate();
            _rendered = _content;
        }

        _window!.Show(Input.mousePosition, new Vector2(32f, 0f), fixTooltipBounds: true);
        _window.ToFront(force: true);
    }

    internal void Hide()
    {
        _content = null;
        if (_window?.handler != null && _window.IsVisible()) _window.Hide();
    }

    private bool EnsureWindow()
    {
        if (_window?.handler != null) return true;
        ReleaseWindow();
        var root = RenderHandler.current?.overlayRoot;
        if (root == null) return false;
        _window = new PixelWindow();
        _window.handler.name = "Digital Storage Partition Lore";
        _window.handler.SetParent(root);
        _element = new RichTextElement(canResize: false, -1, -1, hasRaycast: false);
        _element.SetPadding(Padding, Padding, Padding, Padding);
        _window.Attach(_element);
        _rendered = null;
        return true;
    }

    private void ReleaseWindow()
    {
        if (_window == null) return;
        _window.Destroy();
        // Destroy skips cleanup after Unity has destroyed the handler; these lists still hold the wrapper.
        GraphHandler.windowGraphNodeRoots?.Remove(_window);
        WindowsHandler.current?.visibleWindows?.Remove(_window);
        _window = null;
        _element = null;
        _rendered = null;
    }

    public void Dispose()
    {
        ReleaseWindow();
        _content = null;
    }
}
