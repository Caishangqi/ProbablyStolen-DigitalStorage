using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Integration;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class TerminalDecorationRefreshTests
{
    [Test]
    public void DirtyEvents_MergeIntoOneDrawOfTheCurrentPageWithoutChangingDragMapping()
    {
        var fixture = new Fixture();
        var shown = fixture.Screen.GetPage();
        fixture.Refresh.Show();
        fixture.MarkDirty();
        fixture.MarkDirty();
        fixture.Catalog.OnSettingsApplied();
        fixture.Draw();

        Assert.That(fixture.FrameCallbacks, Has.Count.EqualTo(1));
        Assert.That(fixture.Drawn, Has.Count.EqualTo(1));
        fixture.FrameCallbacks[0]();
        Assert.That(fixture.Drawn, Is.EqualTo(new[] { shown, shown }));
        var current = fixture.Screen.GetPage();
        Assert.That(current, Is.SameAs(shown));
        var batch = fixture.Menu.CreateDragBatch(shown, current, new[] { shown.Slots[0].Group!.Id }, current.PresentationRevision);
        Assert.That(batch!.RequestedPieces, Is.EqualTo(2));
    }

    [Test]
    public void Render_NewSnapshotWithTheSameMappingReevaluatesTheExactPage()
    {
        var fixture = new Fixture();
        var before = fixture.Screen.GetPage();
        fixture.Refresh.Show();
        fixture.MarkDirty();
        fixture.UpdateAmount(5);
        var after = fixture.Screen.GetPage();
        Assert.That(after, Is.Not.SameAs(before));
        Assert.That(after.ViewRevision, Is.EqualTo(before.ViewRevision));

        fixture.Draw();
        fixture.FrameCallbacks[0]();

        Assert.That(fixture.Drawn, Is.EqualTo(new[] { before, after }));
        fixture.Available = false;
        fixture.Draw();
        Assert.That(fixture.Drawn, Is.EqualTo(new[] { before, after, after }));
    }

    [Test]
    public void SamplesChanged_RedrawsAnEquivalentPageWithTheNewSampleAndClearsAnIneligibleSample()
    {
        var fixture = new Fixture();
        var page = fixture.Screen.GetPage();
        fixture.Refresh.Show();
        fixture.Sample = new GameItem("replacement");
        fixture.Refresh.SamplesChanged();
        fixture.FrameCallbacks[0]();
        fixture.Sample = null;
        fixture.Refresh.SamplesChanged();
        fixture.FrameCallbacks[1]();

        Assert.That(fixture.Tints, Is.EqualTo(new int?[] { 7, 9, null }));
        Assert.That(fixture.Drawn, Is.EqualTo(new[] { page, page, page }));
        Assert.That(fixture.Screen.GetPage(), Is.SameAs(page));
    }

    [Test]
    public void QueuedRedraw_UsesTheDisplayedPageWhenSearchConfigurationChanged()
    {
        var fixture = new Fixture();
        var shown = fixture.Screen.GetPage();
        fixture.Refresh.Show();
        fixture.MarkDirty();
        fixture.Search.UpdateConfiguration(new SearchConfiguration());
        fixture.FrameCallbacks[0]();

        Assert.That(fixture.Drawn, Is.EqualTo(new[] { shown, shown }));
        Assert.That(fixture.Screen.ViewRevision, Is.EqualTo(shown.ViewRevision));
        Assert.That(fixture.Screen.ScrollRow, Is.EqualTo(shown.ScrollRow));
        Assert.That(fixture.Screen.GetPage().ViewRevision, Is.GreaterThan(shown.ViewRevision));
    }

    [Test]
    public void Hide_CancelsAndDetachesAndShowRebuildsWithTheCurrentRevision()
    {
        var fixture = new Fixture();
        fixture.Refresh.Show();
        fixture.MarkDirty();
        var stale = fixture.FrameCallbacks[0];
        fixture.Refresh.Hide();
        fixture.Refresh.Hide();
        fixture.MarkDirty();
        stale();

        Assert.That(fixture.Canceled, Is.EqualTo(1));
        Assert.That(fixture.Cleared, Is.EqualTo(1));
        Assert.That(fixture.FrameCallbacks, Has.Count.EqualTo(1));
        Assert.That(fixture.Drawn, Has.Count.EqualTo(1));

        fixture.Refresh.Show();
        fixture.Refresh.Show();
        fixture.MarkDirty();
        stale();
        Assert.That(fixture.Drawn, Has.Count.EqualTo(2));
        Assert.That(fixture.FrameCallbacks, Has.Count.EqualTo(2));
        fixture.FrameCallbacks[1]();
        Assert.That(fixture.Drawn, Has.Count.EqualTo(3));
    }

    private sealed class Fixture
    {
        private const string Plugin = "plugin";
        private static readonly ItemKey Key = new("apple");

        internal Fixture()
        {
            Screen = new StorageScreen(2, 1, search: Search);
            UpdateAmount(2);
            var first = Sample;
            Catalog.Activate(Plugin, new[] { new IntegrationDecorationCatalog.Contribution(Plugin + "/border", "border",
                items => new int?[] { ReferenceEquals(items[0], first) ? 7 : 9 }) });
            Refresh = new TerminalDecorationRefresh(Catalog, OnNextFrame, DrawDecorations, () => Cleared++);
            Draw();
        }

        internal IntegrationDecorationCatalog Catalog { get; } = new();
        internal TerminalSearchService Search { get; } = new(new SearchMatcherRegistry(), new SearchConfiguration());
        internal StorageScreen Screen { get; }
        internal StorageMenu Menu { get; } = new();
        internal TerminalDecorationRefresh Refresh { get; }
        internal List<Action> FrameCallbacks { get; } = new();
        internal List<StorageScreenPage> Drawn { get; } = new();
        internal List<int?> Tints { get; } = new();
        internal GameItem? Sample { get; set; } = new GameItem("apple");
        internal bool Available { get; set; } = true;
        internal int Canceled { get; private set; }
        internal int Cleared { get; private set; }

        internal void UpdateAmount(int amount)
        {
            Menu.BroadcastChanges(new TerminalInventorySnapshot(true, new[] { new TerminalEntry(Key, amount, "Apple", "sprite") }));
            Screen.UpdateEntriesInOrder(Menu.GetEntries());
        }

        internal void MarkDirty() => Catalog.Invalidate(Plugin);

        internal void Draw() => Refresh.SetDisplayedPage(Screen.GetPage(), Available);

        private void DrawDecorations(StorageScreenPage page, bool storageAvailable)
        {
            var external = Catalog.EvaluatePage(page, storageAvailable, _ => Sample);
            var result = StoragePresentationPipeline.Empty.DecoratePage(page, null, external);
            Tints.Add(result[0].Count == 0 ? null : result[0][0].TintRgb);
            Drawn.Add(page);
        }

        private Action OnNextFrame(Action callback)
        {
            FrameCallbacks.Add(callback);
            return () => Canceled++;
        }
    }
}
