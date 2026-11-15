using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal.Stacking;

[TestFixture]
public sealed class StackingFeatureTests
{
    private static readonly string[] Installed = { StackingModule.ModuleId };

    [Test]
    public void Projector_IsActiveOnlyWithModuleSwitchAndAnIgnoreRule()
    {
        var projector = new StackingProjector(_ => { }, () => false);
        var data = new MemoryData();
        Assert.That(projector.IsActive(Context(data, Installed)), Is.False, "No rules yet.");

        Save(data, Ignore(StackingFieldCatalog.ContainerContent));
        Assert.That(projector.IsActive(Context(data, Installed)), Is.True);
        Assert.That(projector.IsActive(Context(data, Array.Empty<string>())), Is.False, "Module removed.");

        Save(data, Ignore(StackingFieldCatalog.ContainerContent).WithEnabled(false));
        Assert.That(projector.IsActive(Context(data, Installed)), Is.False, "Switched off.");

        data.Values[StackingModule.ConfigTag] = "garbage";
        Assert.That(projector.IsActive(Context(data, Installed)), Is.False, "Unreadable settings never merge.");
    }

    [Test]
    public void Projector_MergesBottlesWhenContentAndValueAreIgnored()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.ContainerContent, StackingFieldCatalog.AppraisedValue));
        var entries = new[] { Bottle(9, 4), Bottle(4, 5), Bottle(0, 2) };

        var groups = new StackingProjector(_ => { }, () => false).Project(entries, Context(data, Installed), EntryAttributes.Empty);

        Assert.That(groups.Single().Members, Has.Count.EqualTo(3));
    }

    [Test]
    public void Projector_KeepsEntriesApartWithoutTheValueThatIsStillCompared()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.ContainerContent));
        var full = Bottle(9, 4);
        var empty = Bottle(0, 2);
        var attributes = new EntryAttributes(new Dictionary<ItemKey, IReadOnlyDictionary<string, long>>
        {
            [full.Key] = new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = 55 },
            [empty.Key] = new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = 1 }
        });

        var groups = new StackingProjector(_ => { }, () => false).Project(new[] { full, empty }, Context(data, Installed), attributes);

        Assert.That(groups, Has.Count.EqualTo(2));
    }

    [Test]
    public void Projector_SplitsAGroupWhosePieceCountWouldOverflow()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.ContainerContent, StackingFieldCatalog.AppraisedValue));
        var entries = new[] { Bottle(1, long.MaxValue - 1), Bottle(2, 5) };

        var groups = new StackingProjector(_ => { }, () => false).Project(entries, Context(data, Installed), EntryAttributes.Empty);

        Assert.That(groups.Select(group => group.Members.Count), Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void Decorator_LaysTheTintedMarkOnlyOverMixedGroups()
    {
        var decorator = new MixedGroupDecorator();
        var mixed = new DisplayEntry("g", new[] { Bottle(1, 1), Bottle(2, 1) });
        var data = new MemoryData();
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), new[] { decorator },
            Array.Empty<IEntryTooltipContributor>(), Array.Empty<TooltipPanelDefinition>(), _ => { });

        var mark = pipeline.Decorate(mixed, Context(data, Installed)).Single();
        Assert.That(mark.OverlayKey, Is.EqualTo(MixedGroupDecorator.OverlayKey));
        Assert.That(mark.TintRgb, Is.EqualTo(StackingMarker.Default.Rgb));

        Save(data, StackingConfig.Default.WithMarker(new StackingMarker(0xB37FEB)));
        Assert.That(pipeline.Decorate(mixed, Context(data, Installed)).Single().TintRgb, Is.EqualTo(0xB37FEB));
        Assert.That(pipeline.Decorate(DisplayEntry.Single(Bottle(3, 1)), Context(data, Installed)), Is.Empty);
        Assert.That(pipeline.Decorate(mixed, Context(data, Array.Empty<string>())), Is.Empty);
    }

    [Test]
    public void UnitTotalTooltip_AddsTheUnitCountOnlyWhenPiecesHoldSeveralUnits()
    {
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new[] { new UnitTotalTooltip() }, Array.Empty<TooltipPanelDefinition>(), _ => { });
        var context = Context(new MemoryData(), Array.Empty<string>());

        var panels = pipeline.Tooltip(DisplayEntry.Single(new TerminalEntry(new ItemKey("fuel"), 3, "Fuel", "s", totalUnits: 70)),
            context);
        Assert.That(panels.Single().Definition.Id, Is.EqualTo(TooltipPanelDefinition.HintId));
        Assert.That(panels.Single().Lines.Single().Args.Single(), Is.EqualTo(70L));
        Assert.That(pipeline.Tooltip(DisplayEntry.Single(new TerminalEntry(new ItemKey("ingot"), 3, "Ingot", "s")), context),
            Is.Empty);
    }

    [Test]
    public void InfoTooltip_ListsCountAndAppliedIgnoreRulesForMixedGroupsOnly()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.ContainerContent, StackingFieldCatalog.MetalPurity, StackingFieldCatalog.AppraisedValue));
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new[] { new StackingInfoTooltip() }, new[] { StackingInfoTooltip.Panel }, _ => { });
        var mixed = new DisplayEntry("g", new[] { Bottle(1, 1), Bottle(2, 1), Bottle(3, 1) });

        var panel = pipeline.Tooltip(mixed, Context(data, Installed)).Single();

        Assert.That(panel.Definition.Id, Is.EqualTo(StackingInfoTooltip.PanelId));
        Assert.That(panel.Definition.BorderRgb, Is.EqualTo(0xC3A6FF), "Set apart by its border.");
        Assert.That(panel.Definition.TitleRgb, Is.EqualTo(TooltipPanelDefinition.Hint.TitleRgb), "Text keeps the game colors.");
        Assert.That(panel.Lines.Select(line => (line.Role, line.TextKey)), Is.EqualTo(new[]
        {
            (TooltipRole.Title, StackingInfoTooltip.TitleKey), (TooltipRole.Label, StackingInfoTooltip.KindsKey),
            (TooltipRole.Label, StackingInfoTooltip.RulesKey), (TooltipRole.Body, StackingInfoTooltip.RuleKey),
            (TooltipRole.Body, StackingInfoTooltip.RuleKey)
        }));
        Assert.That(panel.Lines[1].Args.Single(), Is.EqualTo(3));
        var names = panel.Lines.Skip(3).Select(line => ((LocalizedText)line.Args.Single()).TextKey);
        Assert.That(names, Is.EqualTo(new[]
        {
            StackingFieldCatalog.Find(StackingFieldCatalog.ContainerContent)!.NameKey,
            StackingFieldCatalog.Find(StackingFieldCatalog.AppraisedValue)!.NameKey
        }));
        Assert.That(pipeline.Tooltip(DisplayEntry.Single(Bottle(1, 1)), Context(data, Installed)), Is.Empty);
        Assert.That(pipeline.Tooltip(mixed, Context(data, Array.Empty<string>())), Is.Empty);
    }

    [Test]
    public void Editor_SetsMarkerColorAndRejectsInvalidText()
    {
        var data = new MemoryData();
        var saves = 0;
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => saves++);

        Assert.That(editor.SetMarkerColor("#abc"), Is.True);
        Assert.That(editor.Config.Marker.Rgb, Is.EqualTo(0xAABBCC));
        Assert.That(saves, Is.EqualTo(1));
        Assert.That(editor.SetMarkerColor("blue"), Is.False);
        Assert.That(editor.LastError, Is.EqualTo(StackingRulesEditor.MarkerInvalidKey));
        Assert.That(editor.Config.Marker.Rgb, Is.EqualTo(0xAABBCC));
        Assert.That(editor.SetMarkerColor("#AABBCC"), Is.True);
        Assert.That(editor.LastError, Is.Null);
        Assert.That(saves, Is.EqualTo(1), "The same color is not saved again.");
        data.RefuseWrites = true;
        Assert.That(editor.SetMarkerColor("#FF0000"), Is.False);
        Assert.That(editor.LastError, Is.EqualTo(StackingRulesEditor.SaveFailedKey));
    }

    [Test]
    public void Feature_RegistersAStorageRailButtonVisibleWithTheModule()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _ => { });
        StackingFeature.Register(registry, window => window, _ => { }, () => false);
        var button = registry.Buttons.Single(entry => entry.Button.Id == StackingFeature.ActionId);
        Assert.That(button.Order, Is.EqualTo(20));
        Assert.That(button.TerminalId, Is.EqualTo(TerminalRegistry.StorageTerminalName));
        Assert.That(button.IsVisible(Context(new MemoryData(), Installed)), Is.True);
        Assert.That(button.IsVisible(Context(new MemoryData(), Array.Empty<string>())), Is.False);
        Assert.That(registry.StorageProjectors.Single().Id, Is.EqualTo(StackingProjector.ProjectorId));
    }

    [Test]
    public void Store_CommitCountsOnlyWhenTheTagReadsBack()
    {
        var data = new MemoryData();
        var store = new StackingConfigStore(data);
        Assert.That(store.Read().Status, Is.EqualTo(StackingConfigStatus.Missing));
        Assert.That(data.Values, Is.Empty, "Reading never creates the tag.");

        Assert.That(store.TryCommit(Ignore(StackingFieldCatalog.Stolen)), Is.True);
        data.RefuseWrites = true;
        Assert.That(store.TryCommit(StackingConfig.Default), Is.False);
        Assert.That(store.Effective(), Is.EqualTo(Ignore(StackingFieldCatalog.Stolen)));

        data.RefuseWrites = false;
        data.Values[StackingModule.ConfigTag] = "v9";
        Assert.That(store.TryCommit(StackingConfig.Default), Is.False, "Unreadable text is never overwritten.");
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo("v9"));
    }

    [Test]
    public void Editor_AddsIgnoreRowsLocatesExistingOnesAndReportsRefusedSaves()
    {
        var data = new MemoryData();
        var saves = 0;
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => saves++);

        Assert.That(editor.AddOrLocate(StackingFieldCatalog.PriceComposition), Is.EqualTo(0));
        Assert.That(editor.Config.ModeOf(StackingFieldCatalog.PriceComposition), Is.EqualTo(StackingMode.Ignore));
        Assert.That(editor.AddOrLocate(StackingFieldCatalog.MetalPurity), Is.EqualTo(1));
        Assert.That(editor.AddOrLocate(StackingFieldCatalog.PriceComposition), Is.EqualTo(0));
        Assert.That(editor.ReplaceField(1, StackingFieldCatalog.PriceComposition), Is.EqualTo(0), "Locates, never merges.");
        Assert.That(editor.Config.Rules.Select(rule => rule.FieldId),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition, StackingFieldCatalog.MetalPurity }));
        Assert.That(editor.SetMode(1, StackingMode.Match), Is.True);
        Assert.That(editor.SetEnabled(false), Is.True);
        Assert.That(editor.Config.Rules, Has.Count.EqualTo(2), "Switching off keeps the rows.");
        Assert.That(editor.Remove(0), Is.True);
        Assert.That(editor.Clear(), Is.True);
        Assert.That(editor.Config.Rules, Is.Empty);
        Assert.That(saves, Is.EqualTo(6));

        data.RefuseWrites = true;
        Assert.That(editor.AddOrLocate(StackingFieldCatalog.Heat), Is.EqualTo(-1));
        Assert.That(editor.LastError, Is.EqualTo(StackingRulesEditor.SaveFailedKey));
        Assert.That(editor.Config.Rules, Is.Empty);
    }

    [Test]
    public void Editor_RefusesEveryEditWhileSettingsAreUnreadable()
    {
        var data = new MemoryData();
        data.Values[StackingModule.ConfigTag] = "broken";
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => { });
        Assert.That(editor.IsEditable, Is.False);
        Assert.That(editor.Clear(), Is.False);
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo("broken"));
    }

    [Test]
    public void Summary_DescribesTheRulesAndRowNotes()
    {
        Assert.That(StackingRuleSummary.Describe(StackingConfig.Default).Key, Is.EqualTo(StackingRuleSummary.ExactKey));
        Assert.That(StackingRuleSummary.Describe(StackingConfig.Default.WithEnabled(false)).Key,
            Is.EqualTo(StackingRuleSummary.DisabledKey));
        var config = new StackingConfig(true, new[]
        {
            new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Match),
            new StackingRule(StackingFieldCatalog.ContainerContent, StackingMode.Ignore)
        });
        Assert.That(StackingRuleSummary.Describe(config).IgnoredFields, Is.EqualTo(new[] { StackingFieldCatalog.ContainerContent }));
        Assert.That(StackingRuleSummary.RowNotes(config, 0), Is.Empty, "Match is useful next to an ignored raw field.");
        Assert.That(StackingRuleSummary.RowNotes(config, 1), Is.EqualTo(new[]
        {
            StackingRuleSummary.ValueStillComparedKey, StackingRuleSummary.ContainerFirstOnlyKey
        }));
        var alone = new StackingConfig(true, new[] { new StackingRule(StackingFieldCatalog.Heat, StackingMode.Match) });
        Assert.That(StackingRuleSummary.RowNotes(alone, 0), Is.EqualTo(new[] { StackingRuleSummary.MatchAloneKey }));
        var valueOnly = Ignore(StackingFieldCatalog.AppraisedValue);
        Assert.That(StackingRuleSummary.RowNotes(valueOnly, 0), Is.EqualTo(new[] { StackingRuleSummary.DerivedAloneKey }));
    }

    private static StackingConfig Ignore(params string[] fields) =>
        new(true, fields.Select(field => new StackingRule(field, StackingMode.Ignore)));

    private static void Save(MemoryData data, StackingConfig config) =>
        data.Values[StackingModule.ConfigTag] = StackingConfigCodec.Encode(config);

    private static TerminalContext Context(ITerminalFeatureData data, IReadOnlyCollection<string> modules) =>
        new(modules, false, TerminalRegistry.StorageTerminalName, new NoCommands(), new object(), data);

    private static TerminalEntry Bottle(int tablets, long pieces)
    {
        var children = Enumerable.Range(0, tablets).Select(index => new CanonicalIdentityChild(
            new CanonicalIdentityInput("water_tablet", Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(),
                Array.Empty<CanonicalIdentityFeature>(), unitValue: 6),
            1, new CanonicalIdentityShape(0, index, 1, 1, false, 0, 0, new byte[] { 0 }),
            new CanonicalIdentityShape(0, index, 1, 1, false, 0, 0, new byte[] { 0 }))).ToArray();
        var key = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput("water_tablet_container", Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>(), unitValue: 1,
            contentWindow: CanonicalIdentityNode.Window(new[]
            {
                CanonicalIdentityNode.Grid(new CanonicalIdentityShape(0, 0, 1, 9, false, 0, 0, new byte[9]), children)
            })));
        return new TerminalEntry(key, pieces, "Bottle", "sprite");
    }

    private sealed class MemoryData : ITerminalFeatureData
    {
        internal Dictionary<string, string> Values { get; } = new();
        internal bool RefuseWrites { get; set; }

        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

        public bool TryWrite(string key, string value)
        {
            if (RefuseWrites) return false;
            Values[key] = value;
            return true;
        }
    }

    private sealed class NoCommands : ITerminalCommands
    {
        public void SwitchToScreen(object subScreen) { }
        public void StoreState() { }
        public void Switch(string terminalName) { }
        public void NotifySettingChanged(string settingName) { }
    }
}
