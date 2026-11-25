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
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal.Stacking;

[TestFixture]
public sealed class StackingFeatureTests
{
    private static readonly string[] Installed = { StackingModule.ModuleId };

    [Test]
    public void Projector_IsActiveOnlyWithModuleAndAnActiveItemRule()
    {
        var projector = new StackingProjector(_ => { }, () => false);
        var data = new MemoryData();
        Assert.That(projector.IsActive(Context(data, Installed)), Is.False, "No rules yet.");

        Save(data, Ignore(StackingFieldCatalog.ContainerContent));
        Assert.That(projector.IsActive(Context(data, Installed)), Is.True);
        Assert.That(projector.IsActive(Context(data, Array.Empty<string>())), Is.False, "Module removed.");

        Save(data, Ignore(StackingFieldCatalog.ContainerContent).WithAllEnabled(false));
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
    public void Projector_LogsSplitsOnlyForTheActiveItemRule()
    {
        var data = new MemoryData();
        var configured = StackingConfig.Default.WithItem(new StackingItemRule("water_tablet_container", true,
            new[] { new StackingRule(StackingFieldCatalog.ContainerContent, StackingMode.Ignore) }));
        Save(data, configured);
        var full = Bottle(9, 1);
        var empty = Bottle(0, 1);
        var otherA = Tagged("scrap_metal", Tag("A"));
        var otherB = Tagged("scrap_metal", Tag("B"));
        var entries = new[] { full, empty, otherA, otherB };
        var attributes = Values((full, 55), (empty, 1), (otherA, 10), (otherB, 10));
        var logs = new List<string>();
        var projector = new StackingProjector(logs.Add, () => true);

        projector.Project(entries, Context(data, Installed), attributes);
        Save(data, configured.WithItem(new StackingItemRule("ore", true,
            new[] { new StackingRule("tag:grade", StackingMode.Ignore) })));
        projector.Project(entries, Context(data, Installed), attributes);

        Assert.That(logs, Is.EqualTo(new[] { "stacking_split item=water_tablet_container groups=2 differs=derived" }));
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
        Save(data, Ignore(StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue).WithAdvanced(false));
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
            .Any(name => name.TextKey == StackingInfoTooltip.SelectionHiddenFeatureKey), Is.True,
            "Without the advanced option the hidden difference is named only as hidden.");
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
    public void Draft_MenuOptionsIncludeStoredRawTagsOfItsItemOnly()
    {
        var draft = new StackingItemDraft(StackingConfig.Default, "scrap_metal");
        var own = Tagged("scrap_metal", new CanonicalIdentityTag("AGRI_PERFUME_RECORDED", enabled: true, valueString: "private"));
        var other = Tagged("fruit", new CanonicalIdentityTag("FRUIT_ONLY", enabled: true, valueString: "x"));

        var options = draft.MenuOptions(new[] { own.Key, other.Key }, includeHidden: false);
        var option = options.Single(item => item.Field.Id == "tag:AGRI_PERFUME_RECORDED");

        Assert.That(option.Field.TagKey, Is.EqualTo("AGRI_PERFUME_RECORDED"));
        Assert.That(option.Field.NameKey, Is.Empty);
        Assert.That(options.Select(item => item.Field.Id), Does.Not.Contain("tag:FRUIT_ONLY"));
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
    public void Feature_RegistersTheRulesButtonLeftAndQuickActionsRight()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _ => { });
        StackingFeature.Register(registry, new FakePlatform(), _ => { }, () => false);
        var button = registry.Buttons.Single(entry => entry.Button.Id == StackingFeature.ActionId);
        Assert.That(button.Order, Is.EqualTo(20));
        Assert.That(button.Side, Is.EqualTo(DockSide.Left));
        Assert.That(button.Scope, Is.EqualTo(ActionScope.View(TerminalRegistry.StorageTerminalName)));
        Assert.That(button.IsVisible(Context(new MemoryData(), Installed)), Is.True);
        Assert.That(button.IsVisible(Context(new MemoryData(), Array.Empty<string>())), Is.False);
        Assert.That(registry.Buttons.Where(entry => entry.Side == DockSide.Right).Select(entry => entry.Button.Id),
            Is.EquivalentTo(new[]
            {
                StackingFeature.AddActionId, StackingFeature.AppendActionId, StackingFeature.EditActionId,
                StackingFeature.DeleteActionId
            }));
        Assert.That(registry.StorageProjectors.Single().Id, Is.EqualTo(StackingProjector.ProjectorId));
    }

    [Test]
    public void Feature_PutsEachPageBackButtonOnTheLeftDockTitledActions()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _ => { });
        registry.AddDock(DockSide.Left, StorageTerminalFeature.LeftDockTitleKey);
        registry.AddDock(DockSide.Right, StorageTerminalFeature.RightDockTitleKey);
        registry.AddTerminal(new TerminalDefinition(TerminalRegistry.StorageTerminalName, 0, "storage", _ => true, _ => new object()));
        StackingFeature.Register(registry, new FakePlatform(), _ => { }, () => false);
        registry.Freeze();
        var left = new TerminalDocks(registry, new UniversalTerminalHandler(registry, new MemoryStore(), _ => { }), _ => { })
            .Find(DockSide.Left)!;
        var commands = new RecordingCommands();
        var onEditor = new TerminalContext(Installed, false, TerminalRegistry.StorageTerminalName, commands, new object(),
            screenId: StackingFeature.ItemEditorScreenId, screenDepth: 2);

        Assert.That(left.GetButtons(onEditor).Select(button => button.Id),
            Is.EqualTo(new[] { StackingFeature.ItemEditorBackActionId }));
        Assert.That(left.TitleKey(onEditor), Is.EqualTo(StorageTerminalFeature.RightDockTitleKey));
        Assert.That(left.TitleKey(Context(new MemoryData(), Installed)), Is.EqualTo(StorageTerminalFeature.LeftDockTitleKey));
        left.Press(onEditor, StackingFeature.ItemEditorBackActionId, false);
        Assert.That(commands.Calls, Is.EqualTo(new[] { "pop" }));
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

    [TestCase(false)]
    [TestCase(true)]
    public void Store_ExpiresEarlierSettingsAndAcceptsNewItemRules(bool refuseClear)
    {
        const string expired = "v3\nunused payload";
        var data = new MemoryData { RefuseWrites = refuseClear };
        data.Values[StackingModule.ConfigTag] = expired;
        var store = new StackingConfigStore(data);

        Assert.That(store.Read().Status, Is.EqualTo(StackingConfigStatus.Missing));
        Assert.That(store.Effective(), Is.EqualTo(StackingConfig.Default));
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo(refuseClear ? expired : string.Empty));

        data.RefuseWrites = false;
        var config = StackingConfig.Default.WithItem(new StackingItemRule("fruit", true,
            new[] { new StackingRule("tag:X", StackingMode.Ignore) }));
        Assert.That(store.TryCommit(config), Is.True);
        Assert.That(new StackingConfigStore(data).Effective(), Is.EqualTo(config));
    }

    [Test]
    public void Editor_FindsItemsByNameIdentifierAndTagKey()
    {
        var config = new StackingConfig(new[]
        {
            new StackingItemRule("cabbage", true, new[] { new StackingRule("tag:AGRI_PERFUME", StackingMode.Ignore) }),
            new StackingItemRule("metal_ingot", true, new[] { new StackingRule(StackingFieldCatalog.PriceComposition, StackingMode.Ignore) }),
            new StackingItemRule("fruit", false, Array.Empty<StackingRule>())
        });
        string Name(string id) => id == "cabbage" ? "白菜" : id == "metal_ingot" ? "金属锭" : id;
        bool Matches(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase);
        string[] Find(string query) => StackingRulesEditor.FindItems(config, query, Name, Matches).Select(item => item.ItemId).ToArray();

        Assert.That(Find(" "), Is.EqualTo(new[] { "cabbage", "metal_ingot", "fruit" }));
        Assert.That(Find("白菜"), Is.EqualTo(new[] { "cabbage" }));
        Assert.That(Find("INGOT"), Is.EqualTo(new[] { "metal_ingot" }));
        Assert.That(Find("perfume"), Is.EqualTo(new[] { "cabbage" }), "Tag keys of the rule are searchable.");
        Assert.That(Find("price"), Is.Empty, "Fixed field identifiers are not tag keys.");
    }

    [Test]
    public void Editor_SwitchesDeletesAndBulkEditsItemRules()
    {
        var data = new MemoryData();
        Save(data, new StackingConfig(new[]
        {
            new StackingItemRule("a", true, new[] { new StackingRule("tag:X", StackingMode.Ignore) }),
            new StackingItemRule("b", true, Array.Empty<StackingRule>())
        }, new StackingMarker(0xFF5C5C)));
        var saves = 0;
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => saves++);

        Assert.That(editor.SetItemEnabled("a", false), Is.True);
        Assert.That(editor.Config.Find("a")!.Rules, Has.Count.EqualTo(1), "Switching off keeps the rows.");
        Assert.That(editor.SetItemEnabled("missing", true), Is.False);
        Assert.That(editor.SetAllEnabled(true), Is.True);
        Assert.That(editor.Config.Items.All(item => item.Enabled), Is.True);
        Assert.That(editor.RemoveItem("b"), Is.True);
        Assert.That(editor.Config.Items.Select(item => item.ItemId), Is.EqualTo(new[] { "a" }));
        Assert.That(editor.RemoveAll(), Is.True);
        Assert.That(editor.Config.Items, Is.Empty);
        Assert.That(editor.Config.Marker.Rgb, Is.EqualTo(0xFF5C5C), "Deleting every rule keeps the marker.");
        Assert.That(saves, Is.EqualTo(4));
    }

    [Test]
    public void Editor_RefusesEveryEditWhileSettingsAreUnreadable()
    {
        var data = new MemoryData();
        data.Values[StackingModule.ConfigTag] = "broken";
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => { });
        Assert.That(editor.IsEditable, Is.False);
        Assert.That(editor.RemoveAll(), Is.False);
        Assert.That(editor.Apply(new StackingItemRule("a", true, Array.Empty<StackingRule>())), Is.False);
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo("broken"));
    }

    [Test]
    public void Draft_EditsInMemoryUntilApplied()
    {
        var data = new MemoryData();
        Save(data, new StackingConfig(new[]
        {
            new StackingItemRule("fruit", false, new[] { new StackingRule("tag:A", StackingMode.Match) })
        }));
        var editor = new StackingRulesEditor(new StackingConfigStore(data), () => { });
        var draft = new StackingItemDraft(editor.Config, "fruit");
        var saved = data.Values[StackingModule.ConfigTag];

        Assert.That(draft.IsNew, Is.False);
        Assert.That(draft.AddOrLocate("tag:B"), Is.EqualTo(1));
        Assert.That(draft.AddOrLocate("tag:A"), Is.EqualTo(0), "An existing row is located, not duplicated.");
        Assert.That(draft.ReplaceField(1, "tag:A"), Is.EqualTo(0), "Locates, never merges.");
        draft.SetMode(1, StackingMode.Match);
        draft.Remove(0);
        Assert.That(data.Values[StackingModule.ConfigTag], Is.EqualTo(saved), "Nothing is saved before apply.");

        Assert.That(editor.Apply(draft.ToRule()), Is.True);
        Assert.That(editor.Config.Find("fruit"), Is.EqualTo(new StackingItemRule("fruit", false,
            new[] { new StackingRule("tag:B", StackingMode.Match) })), "The draft keeps the saved switch.");
        draft.Clear();
        Assert.That(editor.Apply(draft.ToRule()), Is.True);
        Assert.That(editor.Config.Find("fruit")!.Rules, Is.Empty, "An empty rule stays listed.");
        Assert.That(new StackingItemDraft(StackingConfig.Default, "new").ToRule().Enabled, Is.True);
    }

    [Test]
    public void Draft_FilteredRowsKeepTheirOriginalIndices()
    {
        var draft = new StackingItemDraft(StackingConfig.Default, "fruit");
        foreach (var id in new[] { "tag:OTHER", "tag:STOLEN_TAG", StackingFieldCatalog.AppraisedValue, "tag:STOLEN_VALUE_INT" })
            draft.AddOrLocate(id);
        string Name(string id) => id == StackingFieldCatalog.AppraisedValue ? "估价" : id;
        bool Matches(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase);

        Assert.That(StackingRulesEditor.FindRows(draft.Rules, " stolen ", Name, Matches), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(StackingRulesEditor.FindRows(draft.Rules, "估价", Name, Matches), Is.EqualTo(new[] { 2 }));
        Assert.That(StackingRulesEditor.FindRows(draft.Rules, "zzz", Name, Matches, "tag:OTHER"), Is.EqualTo(new[] { 0 }),
            "A just located row stays visible.");
    }

    [Test]
    public void Quick_ClassifiesTheKeptSelection()
    {
        var data = new MemoryData();
        var a = Group("a", Tagged("fruit", Tag("A")));
        var b = Group("b", Tagged("fruit", Tag("B")));
        var other = Group("o", Tagged("other_item", Tag("A")));
        var mixed = new DisplayEntry("m", new[] { Tagged("fruit", Tag("A")), Tagged("fruit", Tag("B")) });

        Assert.That(Kind(data, a, b), Is.EqualTo(StackingQuickKind.AddNew));
        Assert.That(Kind(data, a, other), Is.EqualTo(StackingQuickKind.None), "Different items.");
        Assert.That(Kind(data, a), Is.EqualTo(StackingQuickKind.None), "One plain slot.");
        Assert.That(Kind(data, a, b, other), Is.EqualTo(StackingQuickKind.None), "Three slots, one of another item.");
        Assert.That(Kind(data, a, b, Group("c", Tagged("fruit", Tag("C")))), Is.EqualTo(StackingQuickKind.AddNew),
            "Three slots of one item.");
        Assert.That(Kind(data, mixed), Is.EqualTo(StackingQuickKind.None), "A mixed slot needs a rule of its item.");
        Assert.That(Kind(data, mixed, a), Is.EqualTo(StackingQuickKind.AddNew), "A merged slot can join another slot.");
        Assert.That(Kind(data, mixed, other), Is.EqualTo(StackingQuickKind.None), "Different items.");
        Assert.That(Kind(data), Is.EqualTo(StackingQuickKind.None));

        Save(data, new StackingConfig(new[] { new StackingItemRule("fruit", false, Array.Empty<StackingRule>()) }));
        Assert.That(Kind(data, a, b), Is.EqualTo(StackingQuickKind.AddToExisting), "A switched-off rule still counts.");
        Assert.That(Kind(data, mixed), Is.EqualTo(StackingQuickKind.EditMixed));
        Assert.That(Kind(data, mixed, a), Is.EqualTo(StackingQuickKind.AddToExisting));
        Assert.That(StackingQuickActions.Classify(Context(data, Array.Empty<string>(), a, b),
            new StackingConfigStore(data).Effective()).Kind, Is.EqualTo(StackingQuickKind.None), "Module removed.");
    }

    [Test]
    public void QuickAdd_CreatesAnEnabledRuleThatMergesTheTwoSlots()
    {
        var data = new MemoryData();
        var store = new StackingConfigStore(data);
        var left = Tagged("fruit", Tag("A"));
        var right = Tagged("fruit", Tag("B"));

        var result = StackingQuickActions.AddFromSelection(Context(data, Installed, Group("l", left), Group("r", right)),
            store, Values((left, 20), (right, 20)));

        Assert.That(result, Is.EqualTo(StackingQuickResult.Merged));
        var rule = store.Effective()!.Find("fruit")!;
        Assert.That(rule.Enabled, Is.True);
        Assert.That(rule.Rules, Is.EqualTo(new[] { new StackingRule("tag:VARIANT", StackingMode.Ignore) }));
    }

    [Test]
    public void QuickAdd_FollowsTheEstimatedValueExposedByIgnoringRawData()
    {
        var data = new MemoryData();
        var store = new StackingConfigStore(data);
        var cheap = Priced("fruit", 10);
        var dear = Priced("fruit", 11);

        var result = StackingQuickActions.AddFromSelection(Context(data, Installed, Group("l", cheap), Group("r", dear)),
            store, Values((cheap, 20), (dear, 21)));

        Assert.That(result, Is.EqualTo(StackingQuickResult.Merged));
        Assert.That(store.Effective()!.Find("fruit")!.Rules.Select(rule => rule.FieldId),
            Is.EqualTo(new[] { StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue }));
    }

    [Test]
    public void QuickAdd_AppendsToAnExistingRuleKeepingItsModesAndSwitch()
    {
        var data = new MemoryData();
        Save(data, new StackingConfig(new[]
        {
            new StackingItemRule("fruit", false, new[] { new StackingRule("tag:KEEP", StackingMode.Match) })
        }));
        var store = new StackingConfigStore(data);
        var left = Tagged("fruit", Tag("A"));
        var right = Tagged("fruit", Tag("B"));

        var result = StackingQuickActions.AddFromSelection(Context(data, Installed, Group("l", left), Group("r", right)),
            store, Values((left, 20), (right, 20)));

        Assert.That(result, Is.EqualTo(StackingQuickResult.RuleOff));
        Assert.That(store.Effective()!.Find("fruit"), Is.EqualTo(new StackingItemRule("fruit", false, new[]
        {
            new StackingRule("tag:KEEP", StackingMode.Match), new StackingRule("tag:VARIANT", StackingMode.Ignore)
        })));
    }

    [Test]
    public void QuickAdd_MergesTwoAlreadyMergedGroupsOfOneItem()
    {
        var data = new MemoryData();
        Save(data, new StackingConfig(new[]
        {
            new StackingItemRule("fruit", true, new[] { new StackingRule("tag:VARIANT", StackingMode.Ignore) })
        }));
        var store = new StackingConfigStore(data);
        var cheapA = Tagged("fruit", Tag("A"));
        var cheapB = Tagged("fruit", Tag("B"));
        var dearA = Priced("fruit", 11);
        var dearB = Priced("fruit", 11);
        var cheap = new DisplayEntry("cheap", new[] { cheapA, cheapB });
        var dear = new DisplayEntry("dear", new[] { dearA, dearB });

        Assert.That(Kind(data, cheap, dear), Is.EqualTo(StackingQuickKind.AddToExisting));
        var result = StackingQuickActions.AddFromSelection(Context(data, Installed, cheap, dear), store,
            Values((cheapA, 20), (cheapB, 20), (dearA, 22)));

        Assert.That(result, Is.EqualTo(StackingQuickResult.Merged));
        Assert.That(store.Effective()!.Find("fruit")!.Rules.Select(rule => rule.FieldId), Is.EqualTo(new[]
        {
            "tag:VARIANT", StackingFieldCatalog.AppraisedValue, StackingFieldCatalog.PriceComposition
        }));
    }

    [Test]
    public void QuickAdd_MergesThreeSeparateSlotsWithDifferentDifferences()
    {
        var data = new MemoryData();
        var store = new StackingConfigStore(data);
        var a = Tagged("fruit", Tag("A"));
        var b = Tagged("fruit", Tag("B"));
        var c = Priced("fruit", 11);

        var result = StackingQuickActions.AddFromSelection(
            Context(data, Installed, Group("a", a), Group("b", b), Group("c", c)), store, Values((a, 20), (b, 20), (c, 22)));

        Assert.That(result, Is.EqualTo(StackingQuickResult.Merged));
        Assert.That(store.Effective()!.Find("fruit")!.Rules.Select(rule => rule.FieldId), Is.EquivalentTo(new[]
        {
            "tag:VARIANT", StackingFieldCatalog.PriceComposition, StackingFieldCatalog.AppraisedValue
        }));
    }

    [Test]
    public void QuickAdd_IgnoresHiddenFeaturesOnlyWithTheAdvancedOption()
    {
        var hidden = Featured("scrap_metal", new CanonicalIdentityFeature("CATEGORY_WATER_PURITY", "water", customIntValue1: 1));
        var other = Featured("scrap_metal", new CanonicalIdentityFeature("CATEGORY_WATER_PURITY", "water", customIntValue1: 2));
        var data = new MemoryData();
        Save(data, StackingConfig.Default.WithAdvanced(false));
        var store = new StackingConfigStore(data);
        var selection = new[] { Group("l", hidden), Group("r", other) };

        Assert.That(StackingQuickActions.AddFromSelection(Context(data, Installed, selection), store,
            Values((hidden, 20), (other, 20))), Is.EqualTo(StackingQuickResult.HiddenFeature));
        Assert.That(store.Effective()!.Items, Is.Empty);

        Save(data, StackingConfig.Default);
        Assert.That(StackingQuickActions.AddFromSelection(Context(data, Installed, selection), store,
            Values((hidden, 20), (other, 20))), Is.EqualTo(StackingQuickResult.Merged));
        Assert.That(store.Effective()!.Find("scrap_metal")!.Rules.Single().FieldId,
            Is.EqualTo("feature:CATEGORY_WATER_PURITY"));
    }

    [Test]
    public void BasicEditor_HidesSavedFeatureIdentifiersFromPickerAndSearch()
    {
        const string hiddenId = "feature:secret";
        const string exposedId = "feature:glow";
        var config = StackingConfig.Default.WithItem(new StackingItemRule("lamp", true, new[]
        {
            new StackingRule(hiddenId, StackingMode.Ignore),
            new StackingRule(exposedId, StackingMode.Match)
        }));
        var draft = new StackingItemDraft(config, "lamp");
        var hidden = Featured("lamp", new CanonicalIdentityFeature("secret", ""));
        var exposed = Featured("lamp", new CanonicalIdentityFeature("glow", "", isFeatureExposed: true));
        var options = draft.MenuOptions(new[] { hidden.Key, exposed.Key }, includeHidden: false);
        var visible = options.Select(option => option.Field.Id).ToHashSet(StringComparer.Ordinal);
        string Name(string id) => visible.Contains(id) ? id : "Hidden feature";
        bool Matches(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

        Assert.That(visible, Does.Contain(exposedId).And.Not.Contain(hiddenId));
        Assert.That(draft.Rules.Select(rule => rule.FieldId), Does.Contain(hiddenId));
        Assert.That(StackingRulesEditor.FindRows(draft.Rules, "secret", Name, Matches,
            showsIdentifier: visible.Contains), Is.Empty);
        Assert.That(StackingRulesEditor.FindRows(draft.Rules, "hidden", Name, Matches,
            showsIdentifier: visible.Contains), Is.EqualTo(new[] { 0 }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void QuickAdd_FindsPublicFeatureDifferencesEvenWithAHiddenFirstSlot(bool hiddenFirst)
    {
        var hidden = Featured("lamp", new CanonicalIdentityFeature("glow", "", valueModifier: 0));
        var dim = Featured("lamp", new CanonicalIdentityFeature("glow", "", valueModifier: 1, isFeatureExposed: true));
        var bright = Featured("lamp", new CanonicalIdentityFeature("glow", "", valueModifier: 2, isFeatureExposed: true));
        var selection = new[] { Group("hidden", hidden), Group("dim", dim), Group("bright", bright) };
        if (!hiddenFirst) Array.Reverse(selection);
        var data = new MemoryData();
        Save(data, StackingConfig.Default.WithAdvanced(false));
        var store = new StackingConfigStore(data);

        var result = StackingQuickActions.AddFromSelection(Context(data, Installed, selection), store,
            Values((hidden, 20), (dim, 20), (bright, 20)));

        Assert.That(result, Is.EqualTo(StackingQuickResult.StillSeparate));
        Assert.That(store.Effective()!.Find("lamp")!.Rules.Single().FieldId, Is.EqualTo("feature:glow"));
    }

    [Test]
    public void QuickAdd_SavesNothingWithoutAConfigurableDifferenceOrWhenRefused()
    {
        var data = new MemoryData();
        var store = new StackingConfigStore(data);
        var plain = new TerminalEntry(new ItemKey("fuel"), 1, "Fuel", "sprite");
        var other = new TerminalEntry(new ItemKey("fuel", new Dictionary<string, string> { ["k"] = "2" }), 1, "Fuel", "sprite");

        Assert.That(StackingQuickActions.AddFromSelection(Context(data, Installed, Group("l", plain), Group("r", other)),
            store, EntryAttributes.Empty), Is.EqualTo(StackingQuickResult.NothingConfigurable));
        Assert.That(data.Values, Is.Empty);

        var left = Tagged("fruit", Tag("A"));
        var right = Tagged("fruit", Tag("B"));
        data.RefuseWrites = true;
        Assert.That(StackingQuickActions.AddFromSelection(Context(data, Installed, Group("l", left), Group("r", right)),
            store, Values((left, 20), (right, 20))), Is.EqualTo(StackingQuickResult.SaveFailed));
        Assert.That(StackingQuickActions.AddFromSelection(Context(data, Installed, Group("l", left)), store,
            EntryAttributes.Empty), Is.EqualTo(StackingQuickResult.Unavailable));
    }

    [Test]
    public void QuickDelete_RemovesOnlyTheRowsThatMergeTheSlot()
    {
        var data = new MemoryData();
        var rows = new[]
        {
            new StackingRule("tag:VARIANT", StackingMode.Ignore),
            new StackingRule(StackingFieldCatalog.PriceComposition, StackingMode.Ignore),
            new StackingRule("tag:KEEP", StackingMode.Match)
        };
        Save(data, new StackingConfig(new[] { new StackingItemRule("fruit", true, rows) }));
        var store = new StackingConfigStore(data);
        var a = Tagged("fruit", Tag("A"));
        var b = Tagged("fruit", Tag("B"));
        var mixed = new DisplayEntry("m", new[] { a, b });

        Assert.That(StackingQuickActions.DeleteForSelection(Context(data, Installed, mixed), store, Values((a, 20), (b, 20))),
            Is.EqualTo(StackingQuickActions.DeletedKey));
        Assert.That(store.Effective()!.Find("fruit")!.Rules, Is.EqualTo(new[] { rows[1], rows[2] }),
            "The rows that do not merge this slot stay.");
        Assert.That(StackingQuickActions.DeleteForSelection(Context(data, Installed, mixed), store, Values((a, 20), (b, 20))),
            Is.EqualTo(StackingQuickActions.NothingToDeleteKey), "A stale group has no row left that merges it.");
    }

    [Test]
    public void QuickDelete_RemovesARuleLeftWithoutRowsAndReportsRefusedSaves()
    {
        var data = new MemoryData();
        Save(data, new StackingConfig(new[]
        {
            new StackingItemRule("fruit", true, new[] { new StackingRule("tag:VARIANT", StackingMode.Ignore) }),
            new StackingItemRule("other_item", true, Array.Empty<StackingRule>())
        }));
        var store = new StackingConfigStore(data);
        var a = Tagged("fruit", Tag("A"));
        var b = Tagged("fruit", Tag("B"));
        var mixed = new DisplayEntry("m", new[] { a, b });

        data.RefuseWrites = true;
        Assert.That(StackingQuickActions.DeleteForSelection(Context(data, Installed, mixed), store, Values((a, 20), (b, 20))),
            Is.EqualTo(StackingRulesEditor.SaveFailedKey));
        data.RefuseWrites = false;
        Assert.That(StackingQuickActions.DeleteForSelection(Context(data, Installed, mixed), store, Values((a, 20), (b, 20))),
            Is.EqualTo(StackingQuickActions.DeletedKey));
        Assert.That(store.Effective()!.Items.Select(item => item.ItemId), Is.EqualTo(new[] { "other_item" }));
    }

    [Test]
    public void QuickActions_PressClearsTheSelectionAndReportsTheResult()
    {
        var registry = new TerminalRegistry(new MemoryConfig(), _ => { });
        var platform = new FakePlatform();
        StackingFeature.Register(registry, platform, _ => { }, () => false);
        var data = new MemoryData();
        var commands = new RecordingCommands();
        var left = Tagged("fruit", Tag("A"));
        var right = Tagged("fruit", Tag("B"));
        var context = new TerminalContext(Installed, false, TerminalRegistry.StorageTerminalName, commands, new object(), data,
            selection: new[] { Group("l", left), Group("r", right) });
        var add = registry.Buttons.Single(entry => entry.Button.Id == StackingFeature.AddActionId);

        Assert.That(add.IsVisible(context), Is.True);
        add.Button.OnPress(context, false);

        Assert.That(commands.Calls, Is.EqualTo(new[] { "clear-selection", "message:" + StackingQuickActions.MergedKey }));
        Assert.That(platform.Regrouped, Is.EqualTo(1));
    }

    [Test]
    public void RowNotes_ExplainTheEffectOfIndividualRules()
    {
        var rules = new[]
        {
            new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Match),
            new StackingRule(StackingFieldCatalog.ContainerContent, StackingMode.Ignore)
        };
        Assert.That(StackingRuleSummary.RowNotes(rules, 0), Is.Empty, "Match is useful next to an ignored raw field.");
        Assert.That(StackingRuleSummary.RowNotes(rules, 1), Is.EqualTo(new[]
        {
            StackingRuleSummary.ValueStillComparedKey, StackingRuleSummary.ContainerFirstOnlyKey
        }));
        var alone = new[] { new StackingRule("tag:STOLEN_VALUE_INT", StackingMode.Match) };
        Assert.That(StackingRuleSummary.RowNotes(alone, 0), Is.EqualTo(new[] { StackingRuleSummary.MatchAloneKey }));
        var valueOnly = new[] { new StackingRule(StackingFieldCatalog.AppraisedValue, StackingMode.Ignore) };
        Assert.That(StackingRuleSummary.RowNotes(valueOnly, 0), Is.EqualTo(new[] { StackingRuleSummary.DerivedAloneKey }));
    }

    private static readonly string[] ItemIds = { "water_tablet_container", "scrap_metal", "fruit", "other_item", "fuel", "ingot" };

    /// <summary>Gives every test item the same switched-on ignore rows.</summary>
    private static StackingConfig Ignore(params string[] fields) =>
        new(ItemIds.Select(id => new StackingItemRule(id, true, fields.Select(field => new StackingRule(field, StackingMode.Ignore)))));

    private static void Save(MemoryData data, StackingConfig config) =>
        data.Values[StackingModule.ConfigTag] = StackingConfigCodec.Encode(config);

    private static TerminalContext Context(ITerminalFeatureData data, IReadOnlyCollection<string> modules,
        params DisplayEntry[] selection) =>
        new(modules, false, TerminalRegistry.StorageTerminalName, new NoCommands(), new object(), data, selection: selection);

    private static StackingQuickKind Kind(MemoryData data, params DisplayEntry[] selection) =>
        StackingQuickActions.Classify(Context(data, Installed, selection), new StackingConfigStore(data).Effective()).Kind;

    private static DisplayEntry Group(string id, TerminalEntry entry) => new(id, new[] { entry });

    private static CanonicalIdentityTag Tag(string variant) => new("VARIANT", enabled: true, valueString: variant);

    private static TerminalEntry Priced(string identifier, long unitValue)
    {
        var key = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(identifier, Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(), Array.Empty<CanonicalIdentityFeature>(), unitValue: unitValue));
        return new TerminalEntry(key, 1, identifier, "sprite");
    }

    private static EntryAttributes Values(params (TerminalEntry Entry, long Appraised)[] values) =>
        new(values.ToDictionary(value => value.Entry.Key,
            value => (IReadOnlyDictionary<string, long>)new Dictionary<string, long>
            {
                [StackingFieldCatalog.AppraisedValue] = value.Appraised
            }));

    private sealed class FakePlatform : IStackingPlatform
    {
        internal int Regrouped { get; private set; }

        public ITerminalScreen CreateRulesScreen(object window) => new TestScreen("rules");

        public ITerminalScreen CreateItemEditor(object window, string itemId) => new TestScreen("editor:" + itemId);

        public EntryAttributes ReadAttributes(object window, IReadOnlyList<TerminalEntry> entries) =>
            new(entries.ToDictionary(entry => entry.Key, _ => (IReadOnlyDictionary<string, long>)new Dictionary<string, long>
            {
                [StackingFieldCatalog.AppraisedValue] = 20
            }));

        public void Regroup(object window) => Regrouped++;
    }

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
        public void PushScreen(ITerminalScreen screen) { }
        public void PopScreen() { }
        public void PopToView() { }
        public void ClearSelection() { }
        public void ShowMessage(string textKey) { }
        public void StoreState() { }
        public void Switch(string terminalName) { }
        public void NotifySettingChanged(string settingName) { }
    }
}
