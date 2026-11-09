using System;
using System.Collections.Generic;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class PartitionConfigurationWindow
{
    private const string WindowId = "digital_storage_partition";
    private readonly ModText _text;
    private readonly IReadOnlyList<string> _labels;
    private readonly Func<int, IPartitionWindowSource> _source;
    private readonly Func<ItemKey?> _captureGhost;
    private readonly PartitionWindowController _controller = new();
    private CustomUIWindow? _window;
    private RectTransform? _list;
    private CustomUIElement? _status;
    private TreeNodeRender? _shield;
    private int _selected;
    private static PartitionConfigurationWindow? _current;

    /// <summary>Creates a window whose dropdown entry <c>i</c> edits <c>source(i)</c>.</summary>
    internal PartitionConfigurationWindow(ModText text, IReadOnlyList<string> labels,
        Func<int, IPartitionWindowSource> source, Func<ItemKey?> captureGhost)
    {
        if (labels == null || labels.Count == 0) throw new ArgumentException("At least one cell is required.", nameof(labels));
        _text = text;
        _labels = labels;
        _source = source;
        _captureGhost = captureGhost;
    }

    internal void Open()
    {
        if (CustomUIManager.Instance == null) return;
        CloseCurrent();
        _current = this;
        var builder = CustomUIManager.Window(WindowId, _text.Get("partition_title"), CustomUIManager.LAYER_PAPER)
            .SetSize(380, 440).SetDraggable(true).SetCloseOnEscape().OnClose(OnClose)
            .AddDropdown(new List<string>(_labels), 0, Select)
            .AddLabel("", "status")
            .BeginScroll(260);
        _list = builder.CurrentContainer;
        builder.End().BeginRow()
            .AddLabel(_text.Get("partition_capture"), "capture")
            .AddButton(_text.Get("partition_contents"), Contents)
            .AddButton(_text.Get("partition_clear"), Clear)
            .End().BeginRow()
            .AddButton(_text.Get("partition_apply"), Apply)
            .AddButton(_text.Get("partition_cancel"), Close)
            .End();
        _window = builder.Show();
        _status = _window.Get("status");
        AttachNativeInputShield();
        _window.root.AddComponent<PartitionGhostInputHandler>()
            .Initialize(_window, _shield!, _controller, _captureGhost, Refresh);
        Select(0);
    }

    internal static void CloseCurrent() => _current?.Close();

    private void Select(int index)
    {
        _selected = index;
        _controller.Open(_source(_selected));
        Refresh();
    }

    private void Contents() { _controller.SetFromContents(); Refresh(); }
    private void Clear() { _controller.Clear(); Refresh(); }
    private void Apply() { if (_controller.Apply()) { Close(); return; } Refresh(); }
    private void Remove(ItemKey key) { _controller.Remove(key); Refresh(); }

    private void Refresh()
    {
        if (_window == null || _list == null) return;
        _status?.SetText(_text.Get("partition_" + _controller.Status) + "  " + _controller.Keys.Count + "/63");
        CustomUIBuilder.ClearContainer(_list);
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _list);
        foreach (var key in _controller.Keys)
        {
            var entry = key;
            builder.BeginRow().AddLabel(PartitionIdentityLabel.Format(key))
                .AddButton(_text.Get("partition_remove"), () => Remove(entry)).End();
        }
    }

    private void AttachNativeInputShield()
    {
        if (_window == null) return;
        var shield = TreeNodeRender.InstantiateAsRoot(_window.Rect);
        shield.gameObject.name = "partition_native_input_shield";
        shield.rectTransform.anchorMin = Vector2.zero;
        shield.rectTransform.anchorMax = Vector2.one;
        shield.rectTransform.offsetMin = shield.rectTransform.offsetMax = Vector2.zero;
        shield.rectTransform.SetAsFirstSibling();
        shield.image.sprite = null;
        shield.image.color = new Color(0f, 0f, 0f, 0.004f);
        shield.image.raycastTarget = true;
        // Render node prefabs start with the Image disabled; GraphicRaycaster skips disabled graphics.
        shield.image.enabled = true;
        shield.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        _shield = shield;
    }

    private void Close() { if (_window != null) _window.Close(); else OnClose(); }
    private void OnClose()
    {
        _controller.Close();
        _window = null; _status = null; _list = null; _shield = null;
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
