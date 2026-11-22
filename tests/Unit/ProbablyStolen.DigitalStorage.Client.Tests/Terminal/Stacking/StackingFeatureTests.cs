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
    public void DecoratePage_ComposesExternalAboveStackingAndLeavesTheLayoutUntouched()
    {
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), new[] { new MixedGroupDecorator() },
            Array.Empty<IEntryTooltipContributor>(), Array.Empty<TooltipPanelDefinition>(), _ => { });
        var screen = new StorageScreen(3, 1);
        screen.SetEntriesInOrder(new[]
        {
            new DisplayEntry("mixed", new[] { Bottle(1, 1), Bottle(2, 1) }),
            DisplayEntry.Single(Bottle(3, 0))
        });
        var page = screen.GetPage();
        var context = Context(new MemoryData(), Installed);
        var external = new[]
        {
            new[] { new SlotDecoration("external_bottom", 1), new SlotDecoration("external_top", 2) },
            new[] { new SlotDecoration("zero", 3) },
            new[] { new SlotDecoration("empty", 4) }
        };

        var composed = pipeline.DecoratePage(page, context, external);

        Assert.That(composed[0].Select(item => item.OverlayKey),
            Is.EqualTo(new[] { MixedGroupDecorator.OverlayKey, "external_bottom", "external_top" }));
        Assert.That(composed[1], Is.Empty);
        Assert.That(composed[2], Is.Empty);
        Assert.That(pipeline.DecoratePage(page, context)[0].Single().OverlayKey, Is.EqualTo(MixedGroupDecorator.OverlayKey));
        Assert.That(screen.GetPage(), Is.SameAs(page));
        Assert.Throws<ArgumentException>((Action)(() => pipeline.DecoratePage(page, context, external.Take(1).ToArray())));
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
        Save(data, Ignore(StackingFieldCatalog.ContainerContent, "tag:INGOT_PURITY_TAG", StackingFieldCatalog.AppraisedValue));
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
            (TooltipRole.Label, StackingInfoTooltip.RulesKey), (TooltipRole.Body, StackingInfoTooltip.RuleKey)
        }));
        Assert.That(panel.Lines[1].Args.Single(), Is.EqualTo(3));
        var name = (LocalizedText)panel.Lines[3].Args.Single();
        Assert.That(name.TextKey, Is.EqualTo(StackingFieldCatalog.Find(StackingFieldCatalog.ContainerContent)!.NameKey));
        Assert.That(pipeline.Tooltip(DisplayEntry.Single(Bottle(1, 1)), Context(data, Installed)), Is.Empty);
        Assert.That(pipeline.Tooltip(mixed, Context(data, Array.Empty<string>())), Is.Empty);
    }

    [Test]
    public void InfoTooltip_OmitsUnrelatedAndIdenticalRulesAndUsesCapturedAppraisal()
    {
        var data = new MemoryData();
        Save(data, Ignore("tag:VARIETY", "tag:CHEMISTRY", "tag:METALS", StackingFieldCatalog.AppraisedValue));
        var pipeline = new StoragePresentationPipeline(new[] { new StackingProjector(_ => { }, () => false) },
            Array.Empty<ISlotDecorator>(), new[] { new StackingInfoTooltip() }, new[] { StackingInfoTooltip.Panel }, _ => { });
        var context = Context(data, Installed);
        var chemistry = new CanonicalIdentityTag("CHEMISTRY", enabled: true, valueString: "same");
        var first = Tagged("fruit", chemistry, new CanonicalIdentityTag("VARIETY", enabled: true, valueInt: 1));
        var second = Tagged("fruit", chemistry, new CanonicalIdentityTag("VARIETY", enabled: true, valueInt: 2));
        EntryAttributes CapturedValues(long secondValue) => new(new Dictionary<ItemKey, IReadOnlyDictionary<string, long>>
        {
            [first.Key] = new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = 10 },
            [second.Key] = new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = secondValue }
        });
        var attributes = CapturedValues(20);
        var group = pipeline.Project(new[] { first, second }, context, attributes).Single();
        var entry = new DisplayEntry(group.Id, group.Members);

        var panel = pipeline.Tooltip(entry, context, attributes).Single();
        var names = panel.Lines.Where(line => line.TextKey == StackingInfoTooltip.RuleKey)
            .Select(line => line.Args.Single()).ToArray();
        var refreshedPanel = pipeline.Tooltip(entry, context, CapturedValues(10)).Single();
        var refreshedNames = refreshedPanel.Lines.Where(line => line.TextKey == StackingInfoTooltip.RuleKey)
            .Select(line => line.Args.Single());

        Assert.That(panel.Definition.ExpandToContent, Is.True);
        Assert.That(TooltipPanelDefinition.Hint.ExpandToContent, Is.False);
        Assert.That(names[0], Is.EqualTo("VARIETY"));
        Assert.That(((LocalizedText)names[1]).TextKey, Is.EqualTo("stacking_field_appraised_value"));
        Assert.That(refreshedNames, Is.EqualTo(new object[] { "VARIETY" }));
    }

    [Test]
    public void SelectionInfoTooltip_ListsAllSameIdentifierDifferencesWithoutValues()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue));
        var first = Tagged("scrap_metal", new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "first"));
        var second = Tagged("scrap_metal", new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "second"));
        var third = Tagged("scrap_metal", new CanonicalIdentityTag("A&B_<br>", enabled: true, valueInt: 3));
        var other = Tagged("other_item", new CanonicalIdentityTag("OTHER_ID_TAG", enabled: true, valueInt: 9));
        var groups = new[] { first, second, third, other }.Select(DisplayEntry.Single).ToArray();
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new[] { new StackingInfoTooltip() }, new[] { StackingInfoTooltip.Panel }, _ => { });

        var panel = pipeline.TooltipForSelection(groups[0], Context(data, Installed), groups, EntryAttributes.Empty).Single();
        var differences = panel.Lines.Where(line => line.TextKey == StackingInfoTooltip.SelectionDifferenceKey)
            .Select(line => line.Args.Single()).ToArray();

        Assert.That(panel.Lines.Single(line => line.TextKey == StackingInfoTooltip.SelectionKey).Args.Single(), Is.EqualTo(3));
        Assert.That(differences, Is.EqualTo(new object[] { "A&B_<noparse><</noparse>br>", "AGRI_PERFUME_RECORDED" }));
        Assert.That(panel.Lines.SelectMany(line => line.Args).OfType<string>(),
            Does.Not.Contain("first").And.Not.Contain("second"));
    }

    [Test]
    public void SelectionInfoTooltip_PreservesKnownDifferencesWhenAnotherGroupIsUnreadable()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.PriceComposition));
        var first = DisplayEntry.Single(Tagged("scrap_metal", new CanonicalIdentityTag("COLOR", enabled: true, valueString: "red")));
        var second = DisplayEntry.Single(Tagged("scrap_metal", new CanonicalIdentityTag("COLOR", enabled: true, valueString: "green")));
        var third = DisplayEntry.Single(Tagged("scrap_metal", new CanonicalIdentityTag("COLOR", enabled: true, valueString: "blue")));
        var attributes = new EntryAttributes(new Dictionary<ItemKey, IReadOnlyDictionary<string, long>>
        {
            [second.Representative.Key] = new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = 10 },
            [third.Representative.Key] = new Dictionary<string, long> { [StackingFieldCatalog.AppraisedValue] = 20 }
        });
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new[] { new StackingInfoTooltip() }, new[] { StackingInfoTooltip.Panel }, _ => { });
        var forward = new[] { first, second, third };
        var reverse = new[] { third, second, first };

        var forwardPanel = pipeline.TooltipForSelection(first, Context(data, Installed), forward, attributes).Single();
        var reversePanel = pipeline.TooltipForSelection(third, Context(data, Installed), reverse, attributes).Single();
        var forwardTags = forwardPanel.Lines.Where(line => line.TextKey == StackingInfoTooltip.SelectionDifferenceKey)
            .Select(line => line.Args.Single()).OfType<string>().ToArray();
        var reverseTags = reversePanel.Lines.Where(line => line.TextKey == StackingInfoTooltip.SelectionDifferenceKey)
            .Select(line => line.Args.Single()).OfType<string>().ToArray();

        Assert.That(forwardTags, Is.EqualTo(new object[] { "COLOR" }));
        Assert.That(reverseTags, Is.EqualTo(forwardTags));
        Assert.That(forwardPanel.Lines.SelectMany(line => line.Args).OfType<LocalizedText>()
            .Any(name => name.TextKey == StackingInfoTooltip.SelectionUnknownKey), Is.True);
    }

    [Test]
    public void SelectionInfoTooltip_ReportsExposedFeatureDifferenceAcrossHiddenGroupRegardlessOfOrder()
    {
        var data = new MemoryData();
        Save(data, Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue));
        var hidden = DisplayEntry.Single(Featured("scrap_metal",
            new CanonicalIdentityFeature("glow", "", valueModifier: 0, publicDisplay: "Hidden")));
        var visibleLow = DisplayEntry.Single(Featured("scrap_metal",
            new CanonicalIdentityFeature("glow", "", valueModifier: 1, publicDisplay: "Glow", isFeatureExposed: true, isFeatureDiscovered: true)));
        var visibleHigh = DisplayEntry.Single(Featured("scrap_metal",
            new CanonicalIdentityFeature("glow", "", valueModifier: 9, publicDisplay: "Glow", isFeatureExposed: true, isFeatureDiscovered: true)));
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new[] { new StackingInfoTooltip() }, new[] { StackingInfoTooltip.Panel }, _ => { });
        var forward = new[] { hidden, visibleLow, visibleHigh };
        var reverse = new[] { visibleHigh, visibleLow, hidden };

        var forwardPanel = pipeline.TooltipForSelection(hidden, Context(data, Installed), forward, EntryAttributes.Empty).Single();
        var reversePanel = pipeline.TooltipForSelection(visibleHigh, Context(data, Installed), reverse, EntryAttributes.Empty).Single();
        var forwardFeatures = DifferenceNames(forwardPanel, "stacking_field_feature");
        var reverseFeatures = DifferenceNames(reversePanel, "stacking_field_feature");

        Assert.That(forwardFeatures, Is.EqualTo(new object[] { "glow" }));
        Assert.That(reverseFeatures, Is.EqualTo(forwardFeatures));
        Assert.That(forwardPanel.Lines.SelectMany(line => line.Args).OfType<LocalizedText>()
            .Any(name => name.TextKey == StackingInfoTooltip.SelectionUnknownKey), Is.True);
    }

    [Test]
    public void SelectionInfoTooltip_ReportsUnreadableSettingsWithoutIdentityDetails()
    {
        var data = new MemoryData();
        data.Values[StackingModule.ConfigTag] = "broken";
        var first = DisplayEntry.Single(Tagged("scrap_metal", new CanonicalIdentityTag("TAG_A", enabled: true, valueString: "private")));
        var second = DisplayEntry.Single(Tagged("scrap_metal", new CanonicalIdentityTag("TAG_B", enabled: true, valueString: "secret")));
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new[] { new StackingInfoTooltip() }, new[] { StackingInfoTooltip.Panel }, _ => { });

        var panel = pipeline.TooltipForSelection(first, Context(data, Installed), new[] { first, second }, EntryAttributes.Empty).Single();

        Assert.That(panel.Lines.Select(line => line.TextKey), Does.Contain(StackingInfoTooltip.SelectionUnavailableKey));
        Assert.That(panel.Lines.SelectMany(line => line.Args), Does.Not.Contain("TAG_A").And.Not.Contain("private"));
    }

    [Test]
    public void Editor_MenuOptionsIncludesStoredRawTags()
    {
        var editor = new StackingRulesEditor(new StackingConfigStore(new MemoryData()), () => { });
        var key = Tagged("scrap_metal", new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "private"));

        var option = editor.MenuOptions(new[] { key.Key }).Single(item => item.Field.Id == "tag:AGRI_PERFUME_RECORDED");

        Assert.That(option.Field.TagKey, Is.EqualTo("AGRI_PERFUME_RECORDED"));
        Assert.That(option.Field.NameKey, Is.Empty);
        Assert.That(option.Field.DescriptionKey, Is.Empty);
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

        Assert.That(store.TryCommit(Ignore("tag:STOLEN_TAG")), Is.True);
        data.RefuseWrites = true;
        Assert.That(store.TryCommit(StackingConfig.Default), Is.False);
        Assert.That(store.Effective(), Is.EqualTo(Ignore("tag:STOLEN_TAG")));

        data.RefuseWrites = false;
        data.Values[StackingModule.ConfigTag] = "v9";
        Assert.That(store.TryCommit(StackingConfig.Default), Is.False, "Unreadable text is never overwritten.");
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo("v9"));
    }

    [Test]
    public void Editor_ClearsRetiredSettingsInMemoryUntilExplicitEdit()
    {
        const string legacy = "v2\nenabled\t0\nmarker\t#B37FEB\nrule\tc3RvbGVu\tignore";
        var data = new MemoryData();
        data.Values[StackingModule.ConfigTag] = legacy;
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => { });

        Assert.That(editor.WasReset, Is.True);
        Assert.That(editor.Config, Is.EqualTo(StackingConfig.Default));
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo(legacy));
        Assert.That(editor.AddOrLocate("tag:STOLEN_TAG"), Is.EqualTo(0));
        Assert.That(data.Values[StackingModule.ConfigTag], Does.StartWith("v3\n"));
        Assert.That(editor.WasReset, Is.False);
        Assert.That(editor.Config, Is.EqualTo(Ignore("tag:STOLEN_TAG")));
    }

    [Test]
    public void Editor_FilteredRowsKeepTheirOriginalIndicesForRemoval()
    {
        var data = new MemoryData();
        Save(data, Ignore("tag:OTHER", "tag:STOLEN_TAG", StackingFieldCatalog.AppraisedValue, "tag:STOLEN_VALUE_INT", "tag:A&B_<br>"));
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => { });
        string Name(string id) => id == StackingFieldCatalog.AppraisedValue ? "估价" : id;
        bool Matches(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase);

        var rows = StackingRulesEditor.FindRows(editor.Config, " stolen ", Name, Matches);
        Assert.That(rows, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(StackingRulesEditor.FindRows(editor.Config, "估价", Name, Matches), Is.EqualTo(new[] { 2 }));
        Assert.That(StackingRulesEditor.FindRows(editor.Config, "a&b_<br>", Name, Matches), Is.EqualTo(new[] { 4 }));
        Assert.That(editor.Remove(rows[0]), Is.True);
        Assert.That(editor.Config.Rules.Select(rule => rule.FieldId),
            Is.EqualTo(new[] { "tag:OTHER", StackingFieldCatalog.AppraisedValue, "tag:STOLEN_VALUE_INT", "tag:A&B_<br>" }));
    }

    [Test]
    public void Editor_RevealsAddedAndLocatedRowsWithoutLosingSearchMatches()
    {
        var data = new MemoryData();
        Save(data, Ignore("tag:Q_ONE", "tag:OTHER", "tag:Q_TWO"));
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => { });
        const string query = "q_";
        bool Matches(string value, string search) => value.Contains(search, StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<int> RowsFor(int selection) => StackingRulesEditor.FindRows(editor.Config, query, id => id, Matches,
            editor.Config.Rules[selection].FieldId);

        var added = editor.AddOrLocate(StackingFieldCatalog.ContainerContent);
        Assert.That(RowsFor(added), Is.EqualTo(new[] { 0, 2, 3 }));
        var existing = editor.ReplaceField(added, "tag:OTHER");
        Assert.That(RowsFor(existing), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(RowsFor(editor.AddOrLocate("tag:Q_TWO")), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(StackingRulesEditor.FindRows(editor.Config, query, id => id, Matches), Is.EqualTo(new[] { 0, 2 }));
    }

    [Test]
    public void Editor_AddsIgnoreRowsLocatesExistingOnesAndReportsRefusedSaves()
    {
        var data = new MemoryData();
        var saves = 0;
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => saves++);

        Assert.That(editor.AddOrLocate(StackingFieldCatalog.PriceComposition), Is.EqualTo(0));
        Assert.That(editor.Config.ModeOf(StackingFieldCatalog.PriceComposition), Is.EqualTo(StackingMode.Ignore));
        Assert.That(editor.AddOrLocate("tag:INGOT_PURITY_TAG"), Is.EqualTo(1));
        Assert.That(editor.AddOrLocate(StackingFieldCatalog.PriceComposition), Is.EqualTo(0));
        Assert.That(editor.ReplaceField(1, StackingFieldCatalog.PriceComposition), Is.EqualTo(0), "Locates, never merges.");
        Assert.That(editor.Config.Rules.Select(rule => rule.FieldId),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition, "tag:INGOT_PURITY_TAG" }));
        Assert.That(editor.SetMode(1, StackingMode.Match), Is.True);
        Assert.That(editor.SetEnabled(false), Is.True);
        Assert.That(editor.Config.Rules, Has.Count.EqualTo(2), "Switching off keeps the rows.");
        Assert.That(editor.Remove(0), Is.True);
        Assert.That(editor.Clear(), Is.True);
        Assert.That(editor.Config.Rules, Is.Empty);
        Assert.That(saves, Is.EqualTo(6));

        data.RefuseWrites = true;
        Assert.That(editor.AddOrLocate("tag:STOLEN_VALUE_INT"), Is.EqualTo(-1));
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
    public void RowNotes_ExplainTheEffectOfIndividualRules()
    {
        var config = new StackingConfig(true, new[]
        {
            new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Match),
            new StackingRule(StackingFieldCatalog.ContainerContent, StackingMode.Ignore)
        });
        Assert.That(StackingRuleSummary.RowNotes(config, 0), Is.Empty, "Match is useful next to an ignored raw field.");
        Assert.That(StackingRuleSummary.RowNotes(config, 1), Is.EqualTo(new[]
        {
            StackingRuleSummary.ValueStillComparedKey, StackingRuleSummary.ContainerFirstOnlyKey
        }));
        var alone = new StackingConfig(true, new[] { new StackingRule("tag:STOLEN_VALUE_INT", StackingMode.Match) });
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

    private static IReadOnlyList<object> DifferenceNames(TooltipPanel panel, string textKey) =>
        panel.Lines.Where(line => line.TextKey == StackingInfoTooltip.SelectionDifferenceKey)
            .Select(line => line.Args.Single()).OfType<LocalizedText>().Where(name => name.TextKey == textKey)
            .Select(name => name.Args.Single()).ToArray();

    private static TerminalEntry Tagged(string identifier, params CanonicalIdentityTag[] tags)
    {
        var key = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(identifier, Array.Empty<string>(), tags,
            Array.Empty<CanonicalIdentityFeature>(), unitValue: 10));
        return new TerminalEntry(key, 1, identifier, "sprite");
    }

    private static TerminalEntry Featured(string identifier, CanonicalIdentityFeature feature)
    {
        var key = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(identifier, Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(), new[] { feature }, unitValue: 10));
        return new TerminalEntry(key, 1, identifier, "sprite");
    }

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
