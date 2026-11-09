using ProbablyStolen.DigitalStorage.Client.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class GridNetworkTooltipPresenter
{
    private readonly NativeNetworkScanner _scanner;
    private readonly GridNetworkPowerCoordinator _networkPower;
    private readonly ModText _text;
    private readonly CellTooltipPresenter _cellTooltipPresenter;

    internal GridNetworkTooltipPresenter(NativeNetworkScanner scanner,
        GridNetworkPowerCoordinator networkPower, ModText text, CellTooltipPresenter cellTooltipPresenter)
    {
        _scanner = scanner;
        _networkPower = networkPower;
        _text = text;
        _cellTooltipPresenter = cellTooltipPresenter;
    }

    internal void OnCreateTooltipLate(RichTextBuilder builder, GameItem item)
    {
        if (builder == null || item == null)
        {
            return;
        }

        if (item.identifier == StorageCellDisassemblyRecipe.CellId)
        {
            _cellTooltipPresenter.AddCellLore(builder, item);
            return;
        }

        if (item.identifier == StorageComponent.ItemId)
        {
            return;
        }

        if (!IsNetworkMachine(item))
        {
            return;
        }

        var snapshot = _scanner.Capture(item);
        var grid = snapshot.FindGrid(item);
        var displayStatus = GridNetworkStatusPresenter.Resolve(grid.GetPathingService().GetControllerState(),
            _networkPower.IsPowered(snapshot, grid));
        var statusKey = GetStatusKey(displayStatus);
        builder.AddLine(_text.Get("network_tooltip_state", _text.Get(statusKey)),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);

        if (item.identifier == DriveItem.ItemId)
        {
            var drive = snapshot.FindNode(item);
            builder.AddLine(_text.Get("network_tooltip_drive_slots",
                    drive.OccupiedDriveSlots, drive.DriveSlotCapacity),
                ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
        }

        if (item.GetTagReadonly("IS_RECEIVING_BONUS_FROM_MODULE_BOOL").GetBool())
        {
            builder.AddLine(_text.Get("network_module_stats_scope"),
                ignoreEmpty: true, RenderHandler.ColorPalette.LightBlue);
        }

        AddOpenHint(builder, item);
    }

    /// <summary>
    /// Adds the game's own "Double-Click To Open" line, as ContainerHelper.CreateContainerTooltip does for items with
    /// CONTAINER_TOOLTIP_TAG (Steam build 26496118). The line is added here rather than through the tag because
    /// loading replaces item state with the saved tags, and the tag would then be written into saves.
    /// </summary>
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
        _ => throw new System.ArgumentOutOfRangeException(nameof(status))
    };

    private static bool IsNetworkMachine(GameItem item) =>
        item.identifier == ControllerItem.ItemId ||
        item.identifier == DriveItem.ItemId ||
        item.identifier == TerminalItem.ItemId;
}
