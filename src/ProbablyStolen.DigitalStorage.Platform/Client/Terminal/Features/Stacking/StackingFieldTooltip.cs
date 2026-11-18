using System;
using System.Collections.Generic;
using System.Globalization;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Stacking;

internal sealed class StackingFieldTooltip : IDropdownOptionHover
{
    private static readonly TooltipStyle Style = new(TooltipStyleKind.Rgb, 0x626262, 0x222222, null);
    private readonly IReadOnlyList<(string Title, string Description, string? Absence)> _details;
    private ITooltip? _tooltip;
    private ITooltipPanel? _panel;
    private int _contentIndex = -1;

    internal StackingFieldTooltip(IReadOnlyList<(string Title, string Description, string? Absence)> details) =>
        _details = details;

    public void Show(RectTransform popup, int optionIndex)
    {
        var renderer = RenderHandler.current;
        if (renderer == null || renderer.windowUICanvas == null) return;
        if (_tooltip == null)
        {
            var service = TerminalTooltipPresenter.Service;
            if (service == null) return;
            _tooltip = service is NativeTooltipService native
                ? native.CreatePopupTooltip(popup)
                : service.CreateTooltip(popup.gameObject);
            _panel = _tooltip.AddPanel("stacking_field", 0);
            _panel.SetStyle(Style);
        }

        if (optionIndex != _contentIndex)
        {
            var (title, description, absence) = _details[optionIndex];
            // Native bold is suppressed at 1x UI scale. Separate TMP tags remain intact when the builder wraps the title.
            var content = new RichTextBuilder().Add("<b>").Add(title).AddLine("</b>").AddLine(description);
            if (absence != null) content.AddLine(absence);
            if (_tooltip is NativeTooltipService.TooltipGroup group)
                group.SetSource(optionIndex.ToString(CultureInfo.InvariantCulture));
            _panel!.SetRichText(content);
            _contentIndex = optionIndex;
        }
        _tooltip.Show(popup);
    }

    public bool TryScroll(bool altHeld, float delta, int frameId) =>
        _tooltip is NativeTooltipService.TooltipGroup group && group.Service.TryConsumeScroll(altHeld, delta, frameId, group);

    public void Hide() => _tooltip?.Hide();

    public void Dispose()
    {
        _tooltip?.Dispose();
        _tooltip = null;
        _panel = null;
        _contentIndex = -1;
    }
}
