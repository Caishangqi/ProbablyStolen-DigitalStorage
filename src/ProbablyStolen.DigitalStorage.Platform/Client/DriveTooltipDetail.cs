using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class DriveTooltipDetail : INetworkTooltipDetail
{
    private static readonly Identifier DetailId = new("digital_storage", "drive");
    private readonly ModText _text;
    internal DriveTooltipDetail(ModText text) => _text = text;
    public Identifier Identity => DetailId;

    public void Append(NativeTooltipContext context, Grid grid)
    {
        if (!context.IsActive) return;
        var node = context.Snapshot.FindNode(context.Item);
        if (!context.IsActive || node.Kind != GridNodeKind.Drive) return;
        context.Builder.AddLine(_text.Get("network_tooltip_drive_slots", node.OccupiedDriveSlots, node.DriveSlotCapacity),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
    }
}
