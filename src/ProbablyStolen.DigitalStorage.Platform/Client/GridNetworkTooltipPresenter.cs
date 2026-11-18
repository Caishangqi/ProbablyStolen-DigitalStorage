using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Client.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class GridNetworkTooltipPresenter : INativeTooltipContributor
{
    private static readonly Identifier ContributorId = new("digital_storage", "network");
    private readonly GridNetworkPowerCoordinator _power;
    private readonly ModText _text;
    private IReadOnlyList<INetworkTooltipDetail> _details;
    private readonly Action<Identifier, Exception> _reportError;

    internal GridNetworkTooltipPresenter(GridNetworkPowerCoordinator power, ModText text,
        IReadOnlyList<INetworkTooltipDetail> details, Action<Identifier, Exception> reportError)
    {
        _power = power;
        _text = text;
        _details = details;
        _reportError = reportError;
    }

    internal void SetDetails(IReadOnlyList<INetworkTooltipDetail> details)
    {
        _details = details ?? throw new ArgumentNullException(nameof(details));
    }

    public Identifier Identity => ContributorId;

    public void Append(NativeTooltipContext context)
    {
        var item = context.Item;
        if (!NativeNetworkScanner.IsNetworkMachine(item)) return;
        var snapshot = context.Snapshot;
        if (!context.IsActive) return;
        var grid = snapshot.FindGrid(item);
        var status = GridNetworkStatusPresenter.Resolve(grid.GetPathingService().GetControllerState(),
            _power.IsPowered(snapshot, grid));
        if (!context.IsActive) return;
        context.Builder.AddLine(_text.Get("network_tooltip_state", _text.Get(GetStatusKey(status))),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);

        var node = snapshot.FindNode(item);
        if (node.Kind is GridNodeKind.Drive or GridNodeKind.Terminal)
        {
            if (!context.IsActive) return;
            context.Builder.AddLine(_text.Get("network_tooltip_device_power", node.PowerUsage),
                ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        }

        foreach (var detail in _details)
        {
            if (!context.IsActive) return;
            try { detail.Append(context, grid); }
            catch (Exception error)
            {
                try { _reportError(detail.Identity, error); }
                catch (Exception) { }
            }
        }

        if (!context.IsActive) return;
        AddOpenHint(context.Builder, item);
    }

    private static void AddOpenHint(RichTextBuilder builder, GameItem item)
    {
        if (item.IsTag("CONTAINER_TAG") || item.IsTag("CONTAINER_TOOLTIP_TAG")) return;
        builder.AddLine(" " + LocHelper.GetLocalizedMechanic("mech_container_tooltip"),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
    }

    private static string GetStatusKey(GridNetworkDisplayStatus status) => status switch
    {
        GridNetworkDisplayStatus.Online => "network_status_online",
        GridNetworkDisplayStatus.Unpowered => "network_status_unpowered",
        GridNetworkDisplayStatus.NoController => "network_status_no_controller",
        GridNetworkDisplayStatus.ControllerConflict => "network_status_controller_conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
