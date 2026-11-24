using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Storage;

internal class PixelElement : GraphNodeStorage
{
    private GraphNodeStorage? _renderingParent;
    internal readonly PixelHandler handler = new();
    internal bool IsRenderingChildOf(GraphNodeStorage parent) => ReferenceEquals(_renderingParent, parent);
    internal void SetRenderingParent(GraphNodeStorage parent) => _renderingParent = parent;
    internal void RemoveRenderingParent(GraphNodeStorage parent)
    {
        if (IsRenderingChildOf(parent)) _renderingParent = null;
    }
}

internal sealed class PixelHandler
{
    internal readonly object gameObject = new();
    internal readonly PixelRectTransform rectTransform = new();
}

internal sealed class PixelRectTransform
{
    internal UnityEngine.Vector2 anchoredPosition;
}

internal sealed class ItemMouseHandler
{
    internal readonly GameItem? currentItem = null;
}

internal sealed class PixelWindow : GraphNodeStorage
{
    internal readonly List<GameItem> parentItems = new();
    internal MachineryPanelRow? childElement;
    internal void Validate() { }
}

internal sealed class MachineryPanelRow : GraphNodeStorage
{
    internal GraphNodeStorage? parent;
    internal GameSlotInventory? Component;
    internal MachineryPanel? Modules;
    internal MachineryPanel? Pages;
    internal int ModulePanelReads { get; private set; }
    internal int PagesPanelReads { get; private set; }
    internal int StorageReads { get; private set; }

    internal List<GameInventory> children
    {
        get
        {
            StorageReads++;
            var storage = new List<GameInventory>();
            if (Component != null) storage.Add(Component);
            if (Pages?.content is MachineryTabs tabs) storage.AddRange(tabs.Inventories);
            if (Modules?.content is MachineryIntake { content: { } bay }) storage.Add(bay);
            return storage;
        }
    }

    internal MachineryPanel? GetPanel(string role)
    {
        if (role == CellPages.PanelRole)
        {
            PagesPanelReads++;
            return Pages;
        }
        if (role != CellUpgradeInventory.ModulesPanelRole) return null;
        ModulePanelReads++;
        return Modules;
    }

    internal void Validate() { }
}

internal sealed class MachineryPanel : GraphNodeStorage
{
    internal object? content;
    internal readonly object owner = new();

    internal void SetContent(MachineryTabs tabs)
    {
        if (content is MachineryTabs previous) previous.Unlink();
        content = tabs;
        tabs.Link(this);
    }
}

internal sealed class MachineryIntake : PixelElement
{
    internal GameGridInventory? content;
}

internal sealed class MachineryTabs : PixelElement
{
    private readonly List<MachineryIntake> _tabs = new();
    internal MachineryPanelRow? Row;
    internal object? owner { get; private set; }
    internal int count => _tabs.Count;
    internal IEnumerable<GameGridInventory> Inventories
    {
        get
        {
            foreach (var tab in _tabs)
                if (tab.content is { } inventory) yield return inventory;
        }
    }

    internal MachineryIntake? GetPage(int index) => index >= 0 && index < count ? _tabs[index] : null;
    internal GameGridInventory? GetTabInventory(int index) => GetPage(index)?.content;

    internal void AddTab(string label, MachineryIntake element)
    {
        _tabs.Add(element);
        element.SetRenderingParent(this);
        if (element.content is { } inventory) Link(inventory);
    }

    internal bool Detach(MachineryIntake element)
    {
        if (!_tabs.Remove(element)) return false;
        element.RemoveRenderingParent(this);
        if (owner != null) element.content?.parents.Remove(owner);
        return true;
    }

    internal void Link(MachineryPanel panel)
    {
        SetRenderingParent(panel);
        owner = panel.owner;
        foreach (var inventory in Inventories) Link(inventory);
    }

    internal void Unlink()
    {
        if (owner == null) return;
        foreach (var inventory in Inventories) inventory.parents.Remove(owner);
        owner = null;
    }

    private void Link(GameGridInventory inventory)
    {
        if (owner == null) return;
        inventory.parent = Row;
        inventory.parents.Add(owner);
    }

    internal void Select(int index) { }
    internal void Validate() { }
}

namespace ProbablyStolen.DigitalStorage.Platform.Storage
{
    internal static class ModuleBayExpansionHooks
    {
        internal static void Initialize(GameItem cell) => throw new InvalidOperationException("unexpected bay expansion");
    }
}
