using System;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Client.Tooltips;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Displays partition rule lore through the lifecycle scoped tooltip service.</summary>
internal sealed class PartitionRuleTooltip : IDisposable
{
    private readonly object _owner = new();
    private readonly TooltipVisibilityState _visibility = new();
    private ITooltip? _tooltip;
    private ITooltipPanel? _panel;
    private RichTextBuilder? _content;

    internal void SetContent(RichTextBuilder content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _visibility.Show();
        if (_panel != null) _panel.SetRichText(content);
        else EnsureTooltip();
    }

    internal void FollowPointer()
    {
        if (!_visibility.CanFollow || _content == null) return;
        EnsureTooltip();
        _tooltip?.Show(Input.mousePosition);
    }

    internal void Hide()
    {
        _visibility.Hide();
        _tooltip?.Hide();
    }

    private void EnsureTooltip()
    {
        if (_tooltip != null) return;
        var service = TerminalTooltipPresenter.Service;
        if (service == null) return;
        _tooltip = service.CreateTooltip(_owner);
        _panel = _tooltip.AddPanel("partition_rule", 0);
        _panel.SetStyle(TooltipStyle.Native);
        if (_content != null) _panel.SetRichText(_content);
    }

    public void Dispose()
    {
        Hide();
        _tooltip?.Dispose();
        _tooltip = null;
        _panel = null;
        _content = null;
    }
}
