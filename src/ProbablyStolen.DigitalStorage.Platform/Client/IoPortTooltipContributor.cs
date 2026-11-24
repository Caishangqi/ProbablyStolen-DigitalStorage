using System;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Client.Machines.IoPort;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Adds the IO Port's machine lines to its lore, the way native machines list what they do: the configured rate and
/// fee, then the current direction and state. Reads only the latest machine snapshot, never storage.
/// </summary>
internal sealed class IoPortTooltipContributor : INativeTooltipContributor
{
    private static readonly Identifier ContributorId = new("digital_storage", "io_port");
    private readonly ModText _text;
    private readonly IIoPortTuning _tuning;
    private readonly Func<GameItem, IoPortDisplaySnapshot> _readDisplay;

    internal IoPortTooltipContributor(ModText text, IIoPortTuning tuning, Func<GameItem, IoPortDisplaySnapshot> readDisplay)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _readDisplay = readDisplay ?? throw new ArgumentNullException(nameof(readDisplay));
    }

    public Identifier Identity => ContributorId;

    public void Append(NativeTooltipContext context)
    {
        if (context.Item.identifier != IoPortItem.ItemId || !context.IsActive) return;
        Add(context, _text.Get("io_tooltip_rate", Math.Max(1, _tuning.RootsPerSecond)));
        Add(context, _text.Get("io_tooltip_fee", Math.Max(1, _tuning.RootsPerFee)));
        var state = IoPortPanelState.From(_readDisplay(context.Item));
        if (state.DirectionTextKey != null)
            Add(context, _text.Get("io_tooltip_state", _text.Get(state.DirectionTextKey), _text.Get(state.StatusTextKey)));
    }

    private static void Add(NativeTooltipContext context, string line)
    {
        if (context.IsActive) context.Builder.AddLine(line, ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
    }
}
