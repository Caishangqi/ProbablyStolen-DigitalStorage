using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class ControllerTooltipDetail : INetworkTooltipDetail
{
    private static readonly Identifier DetailId = new("digital_storage", "controller");
    private readonly ModText _text;

    internal ControllerTooltipDetail(ModText text)
    {
        _text = text;
    }

    public Identifier Identity => DetailId;

    public void Append(NativeTooltipContext context, Grid grid)
    {
        if (!context.IsActive || context.Snapshot.FindNode(context.Item).Kind != GridNodeKind.Controller) return;
        context.Builder.AddLine(_text.Get("network_tooltip_total_power", grid.TotalPowerUsage),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
    }
}
