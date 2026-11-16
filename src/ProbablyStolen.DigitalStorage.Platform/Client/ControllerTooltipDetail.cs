using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class ControllerTooltipDetail : INetworkTooltipDetail
{
    private static readonly Identifier DetailId = new("digital_storage", "controller");
    public Identifier Identity => DetailId;

    public void Append(NativeTooltipContext context, Grid grid)
    {
        if (!context.IsActive || context.Snapshot.FindNode(context.Item).Kind != GridNodeKind.Controller ||
            context.IsProjected || !context.IsActive) return;
        context.Builder.Add(LocHelper.GetLocalizedMechanic("mech_machinery_required_energy"),
            RenderHandler.ColorPalette.LightCyan);
        if (!context.IsActive) return;
        context.Builder.AddLine(LocHelper.GetLocalizedMechanic("mech_machinery_per_day", grid.TotalPowerUsage),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
    }
}
