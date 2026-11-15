using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Terminal.Extensions;

[TestFixture]
public sealed class StoragePresentationPipelineTests
{
    private static readonly TerminalEntry Apple = new(new ItemKey("apple"), 2, "Apple", "a");
    private static readonly TerminalEntry Apple2 = new(new ItemKey("apple", new Dictionary<string, string> { ["k"] = "2" }), 3, "Apple", "a");
    private static readonly TerminalEntry Pear = new(new ItemKey("pear"), 1, "Pear", "p");

    [Test]
    public void NoActiveProjector_UsesIdentityProjection()
    {
        var log = new List<string>();
        var pipeline = Pipeline(log, new FakeProjector("p", active: false, groups: null));

        var groups = pipeline.Project(new[] { Apple, Apple2, Pear }, Context(), EntryAttributes.Empty);

        Assert.That(groups.Select(group => group.Members.Count), Is.EqualTo(new[] { 1, 1, 1 }));
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void OneActiveProjector_GroupsEntries()
    {
        var pipeline = Pipeline(new List<string>(), new FakeProjector("p", true,
            entries => new[] { new ProjectedGroup("apples", new[] { Apple, Apple2 }), new ProjectedGroup("pear", new[] { Pear }) }));

        var groups = pipeline.Project(new[] { Apple, Apple2, Pear }, Context(), EntryAttributes.Empty);

        Assert.That(groups.Single(group => group.Id == "apples").Members, Has.Count.EqualTo(2));
    }

    [Test]
    public void SeveralActiveProjectors_FallBackToIdentityAndLog()
    {
        var log = new List<string>();
        var merge = new FakeProjector("a", true, _ => new[] { new ProjectedGroup("all", new[] { Apple, Apple2 }) });
        var pipeline = Pipeline(log, merge, new FakeProjector("b", true, _ => Array.Empty<ProjectedGroup>()));

        var groups = pipeline.Project(new[] { Apple, Apple2 }, Context(), EntryAttributes.Empty);

        Assert.That(groups, Has.Count.EqualTo(2));
        Assert.That(log.Single(), Does.Contain("several_active"));
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("mixed_items")]
    [TestCase("throws")]
    public void InvalidOrFailingProjector_FallsBackToIdentity(string mode)
    {
        var log = new List<string>();
        var projector = new FakeProjector("p", true, _ => mode switch
        {
            "missing" => new[] { new ProjectedGroup("a", new[] { Apple }) },
            "duplicate" => new[] { new ProjectedGroup("a", new[] { Apple, Apple }), new ProjectedGroup("b", new[] { Pear }) },
            "mixed_items" => new[] { new ProjectedGroup("a", new[] { Apple, Pear }) },
            _ => throw new InvalidOperationException()
        });

        var groups = Pipeline(log, projector).Project(new[] { Apple, Pear }, Context(), EntryAttributes.Empty);

        Assert.That(groups.Select(group => group.Members.Single()), Is.EqualTo(new[] { Apple, Pear }));
        Assert.That(log, Has.Count.EqualTo(1));
    }

    [Test]
    public void Decorations_StackByOrderKeepEachDecoratorsOrderAndSkipFailures()
    {
        var log = new List<string>();
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), new ISlotDecorator[]
        {
            new FakeDecorator("late", 10, new SlotDecoration("top", 0xFFFFFF)),
            new FakeDecorator("early", 1, new SlotDecoration("bottom", 0x00FF00), new SlotDecoration("middle", 0x0000FF)),
            new FakeDecorator("broken", 5, null)
        }, Array.Empty<IEntryTooltipContributor>(), Array.Empty<TooltipPanelDefinition>(), log.Add);

        var decorations = pipeline.Decorate(DisplayEntry.Single(Apple), Context());

        Assert.That(decorations.Select(decoration => decoration.OverlayKey), Is.EqualTo(new[] { "bottom", "middle", "top" }));
        Assert.That(decorations[0].TintRgb, Is.EqualTo(0x00FF00));
        Assert.That(log.Count(line => line.Contains("failed=decorator")), Is.EqualTo(1));
    }

    [Test]
    public void Decoration_RefusesMissingKeysAndInvalidTints()
    {
        Assert.Throws<ArgumentException>((Action)(() => _ = new SlotDecoration("", 0)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => _ = new SlotDecoration("x", -1)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => _ = new SlotDecoration("x", 0x1000000)));
    }

    [Test]
    public void Tooltip_GroupsLinesByPanelOrderAndSkipsFailuresAndUnknownPanels()
    {
        var log = new List<string>();
        var info = new TooltipPanelDefinition("info", 10, 1, 2, 3);
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new IEntryTooltipContributor[]
            {
                new FakeContributor("b", 2, "info", "second"), new FakeContributor("a", 1, TooltipPanelDefinition.HintId, "first"),
                new FakeContributor("c", 3, "info", "third"), new FakeContributor("x", 0, "info", null),
                new FakeContributor("lost", 4, "nobody", "lost")
            }, new[] { info }, log.Add);

        var panels = pipeline.Tooltip(DisplayEntry.Single(Apple), Context());

        Assert.That(panels.Select(panel => panel.Definition.Id), Is.EqualTo(new[] { TooltipPanelDefinition.HintId, "info" }));
        Assert.That(panels[1].Lines.Select(line => line.TextKey), Is.EqualTo(new[] { "second", "third" }));
        Assert.That(panels[1].Definition, Is.SameAs(info));
        Assert.That(log.Count(line => line.Contains("failed=tooltip")), Is.EqualTo(1));
        Assert.That(log.Count(line => line.Contains("unknown_panel")), Is.EqualTo(1));
    }

    [Test]
    public void Tooltip_PanelWithoutLinesIsNotShown()
    {
        var pipeline = new StoragePresentationPipeline(Array.Empty<IStorageEntryProjector>(), Array.Empty<ISlotDecorator>(),
            new IEntryTooltipContributor[] { new FakeContributor("a", 0, "info", "line") },
            new[] { new TooltipPanelDefinition("info", 10, 1, 2, 3), new TooltipPanelDefinition("quiet", 20, 1, 2, 3) }, _ => { });

        Assert.That(pipeline.Tooltip(DisplayEntry.Single(Apple), Context()).Select(panel => panel.Definition.Id),
            Is.EqualTo(new[] { "info" }));
        Assert.That(pipeline.HintPanel, Is.SameAs(TooltipPanelDefinition.Hint));
    }

    [Test]
    public void PanelDefinition_PicksColorsByRole()
    {
        var panel = new TooltipPanelDefinition("p", 0, 0x111111, 0x222222, 0x333333);
        Assert.That(panel.ColorOf(TooltipRole.Title), Is.EqualTo(0x111111));
        Assert.That(panel.ColorOf(TooltipRole.Label), Is.EqualTo(0x222222));
        Assert.That(panel.ColorOf(TooltipRole.Body), Is.EqualTo(0x333333));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => _ = new TooltipPanelDefinition("p", 0, 0x1000000, 0, 0)));
        Assert.That(panel.BorderRgb, Is.Null);
        Assert.That(new TooltipPanelDefinition("q", 0, 1, 2, 3, 0xC3A6FF).BorderRgb, Is.EqualTo(0xC3A6FF));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => _ = new TooltipPanelDefinition("p", 0, 0, 0, 0, -1)));
        Assert.That(TooltipPanelDefinition.Hint.BorderRgb, Is.Null);
    }

    [Test]
    public void NoContext_MeansNoExtensions()
    {
        var pipeline = Pipeline(new List<string>(), new FakeProjector("p", true, _ => throw new InvalidOperationException()));
        Assert.That(pipeline.ActiveProjector(null), Is.Null);
        Assert.That(pipeline.Project(new[] { Apple }, null, EntryAttributes.Empty), Has.Count.EqualTo(1));
    }

    [Test]
    public void Invalidate_ChangesTheRevision()
    {
        var pipeline = StoragePresentationPipeline.Empty;
        var before = pipeline.Revision;
        pipeline.Invalidate();
        Assert.That(pipeline.Revision, Is.Not.EqualTo(before));
    }

    [Test]
    public void Registry_RefusesDuplicateExtensionIdsAndRegistrationAfterFreeze()
    {
        var log = new List<string>();
        var registry = new TerminalRegistry(new MemoryConfig(), log.Add);
        registry.AddTerminal(new TerminalDefinition(TerminalRegistry.StorageTerminalName, 0, "s", _ => true, window => window));

        Assert.That(registry.AddStorageProjector(new FakeProjector("x", true, null)), Is.True);
        Assert.That(registry.AddSlotDecorator(new FakeDecorator("x", 0, null)), Is.False);
        Assert.That(registry.AddTooltipContributor(new FakeContributor("y", 0, "info", "line")), Is.True);
        Assert.That(log.Single(), Does.Contain("id=x"));
        Assert.That(registry.AddTooltipPanel(new TooltipPanelDefinition("info", 10, 1, 2, 3)), Is.True);
        Assert.That(registry.AddTooltipPanel(new TooltipPanelDefinition(TooltipPanelDefinition.HintId, 5, 1, 2, 3)), Is.False);
        Assert.That(registry.TooltipPanels.Select(panel => panel.Id), Is.EqualTo(new[] { TooltipPanelDefinition.HintId, "info" }));
        registry.Freeze();
        Assert.Throws<InvalidOperationException>((Action)(() => registry.AddSlotDecorator(new FakeDecorator("z", 0, null))));
        Assert.Throws<InvalidOperationException>((Action)(() =>
            registry.AddTooltipPanel(new TooltipPanelDefinition("late", 0, 1, 2, 3))));
        Assert.That(registry.CreateStoragePipeline(_ => { }).ActiveProjector(Context()), Is.Not.Null);
    }

    private static StoragePresentationPipeline Pipeline(List<string> log, params IStorageEntryProjector[] projectors) =>
        new(projectors, Array.Empty<ISlotDecorator>(), Array.Empty<IEntryTooltipContributor>(),
            Array.Empty<TooltipPanelDefinition>(), log.Add);

    private static TerminalContext Context() =>
        new(Array.Empty<string>(), false, TerminalRegistry.StorageTerminalName, new NoCommands(), new object());

    private sealed class FakeProjector : IStorageEntryProjector
    {
        private readonly bool _active;
        private readonly Func<IReadOnlyList<TerminalEntry>, IReadOnlyList<ProjectedGroup>>? _groups;

        internal FakeProjector(string id, bool active, Func<IReadOnlyList<TerminalEntry>, IReadOnlyList<ProjectedGroup>>? groups)
        {
            Id = id;
            _active = active;
            _groups = groups;
        }

        public string Id { get; }
        public IReadOnlyCollection<string> RequiredAttributes => Array.Empty<string>();
        public bool IsActive(TerminalContext context) => _active;

        public IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries, TerminalContext context,
            EntryAttributes attributes) => _groups!(entries);
    }

    private sealed class FakeDecorator : ISlotDecorator
    {
        private readonly SlotDecoration[]? _decorations;

        internal FakeDecorator(string id, int order, params SlotDecoration[]? decorations)
        {
            Id = id;
            Order = order;
            _decorations = decorations;
        }

        public string Id { get; }
        public int Order { get; }
        public bool IsActive(TerminalContext context) => true;

        public void Decorate(DisplayEntry entry, TerminalContext context, SlotDecorationSink sink)
        {
            if (_decorations == null) throw new InvalidOperationException();
            foreach (var decoration in _decorations) sink.Add(decoration.OverlayKey, decoration.TintRgb);
        }
    }

    private sealed class FakeContributor : IEntryTooltipContributor
    {
        private readonly string _panel;
        private readonly string? _line;

        internal FakeContributor(string id, int order, string panel, string? line)
        {
            Id = id;
            Order = order;
            _panel = panel;
            _line = line;
        }

        public string Id { get; }
        public int Order { get; }
        public bool IsActive(TerminalContext context) => true;

        public void Contribute(DisplayEntry entry, TerminalContext context, EntryTooltipSink sink)
        {
            if (_line == null) throw new InvalidOperationException();
            sink.Body(_panel, _line);
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
