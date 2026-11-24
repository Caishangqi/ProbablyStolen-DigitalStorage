using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

internal static class CellInventoryFixture
{
    internal static GameItem Create(int id = 0)
    {
        var tier = StorageCellTier.All[0];
        var cell = new GameItem(tier.CellId) { uniqueId = id };
        var pages = new MachineryPanel();
        var window = new PixelWindow();
        var row = new MachineryPanelRow { parent = window, Pages = pages };
        window.childElement = row;
        window.parentItems.Add(cell);
        cell.contentWindow = window;

        var component = new GameSlotInventory { identifier = CellContentWindow.SlotId, parent = row };
        component.Place(new GameItem(tier.ComponentId));
        component.parents.Add(pages.owner);
        cell.Slots.Add(component);
        row.Component = component;
        var bay = new GameGridInventory { identifier = CellUpgradeInventory.BayId, parent = row };
        row.Modules = new MachineryPanel { content = new MachineryIntake { content = bay } };

        var tabs = new MachineryTabs { Row = row };
        pages.SetContent(tabs);
        CellPages.AddPage(tabs, PageGeometry.Anchor);
        CellTags.WriteNew(cell);
        return cell;
    }

    internal static MachineryTabs Tabs(GameItem cell) =>
        (MachineryTabs)(cell.contentWindow?.childElement?.Pages?.content ??
            throw new InvalidOperationException("Missing fixture page tabs."));
}
