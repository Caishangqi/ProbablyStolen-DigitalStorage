using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class PageSetSwapTests
{
    // Pages 0, 2 and 4 hold roots; 1 and 3 are empty.
    private const string OriginalText = "A;S1x1x16;S2x2x16;S3x3x16;S4x4x16";
    private static readonly int[] OriginalCounts = { 0, 0, 5, 0, 2 };

    private static PageCompaction Plan(SwapPort port) =>
        PageCompactionPlanner.Plan(port.RecordedLayout!, port.RootCounts());

    [Test]
    public void Run_RemovesEmptyMiddlePagesAndKeepsTheRest()
    {
        var port = new SwapPort();
        var swap = new PageSetSwap<Pages>();

        swap.Run(port, Plan(port));

        Assert.That(port.Recorded.Format(), Is.EqualTo("A;S2x2x16;S4x4x16"));
        Assert.That(port.Current.Kept, Is.EqualTo(new[] { 0, 2, 4 }));
        Assert.That(port.Current.Roots, Is.EqualTo(7), "Every root stays on a kept page.");
        Assert.That(port.Original.Released, Is.True);
        Assert.That(swap.Pending, Is.False);
        Assert.That(swap.Blocked, Is.False);
        port.AssertNoLostTrees();
    }

    [Test]
    public void Run_HousingResetKeepsOnlyTheAnchor()
    {
        var port = new SwapPort("A;L*3", new[] { 0, 0, 0, 0 });
        var swap = new PageSetSwap<Pages>();
        swap.Request();

        swap.Run(port, Plan(port));

        Assert.That(port.Recorded.Format(), Is.EqualTo("A"));
        Assert.That(port.Current.Selected, Is.EqualTo(0));
        Assert.That(swap.IsAtAnchor(port), Is.True);
        port.AssertNoLostTrees();
    }

    [Test]
    public void Run_NothingToRemove_ChangesNothing()
    {
        var port = new SwapPort("A;S1x1x16", new[] { 0, 3 });
        var swap = new PageSetSwap<Pages>();

        swap.Run(port, Plan(port));

        Assert.That(port.Mutations, Is.Zero);
        Assert.That(swap.Pending, Is.False);
    }

    [TestCase("prepare_before")]
    [TestCase("prepare_after")]
    [TestCase("attach_before")]
    [TestCase("attach_detached")]
    [TestCase("attach_assigned")]
    [TestCase("attach_after")]
    [TestCase("verify_new_1")]
    [TestCase("write_new_before")]
    [TestCase("write_new_after")]
    [TestCase("select_before")]
    [TestCase("select_after")]
    [TestCase("verify_new_2")]
    public void Run_StepFailure_RollsBackAndBlocksCompaction(string failure)
    {
        var port = new SwapPort(failure);
        var swap = new PageSetSwap<Pages>();

        Assert.Catch(new Action(() => swap.Run(port, Plan(port))));

        Assert.That(port.Current, Is.SameAs(port.Original));
        Assert.That(port.Recorded.Format(), Is.EqualTo(OriginalText));
        Assert.That(port.Original.PagesMoved, Is.False, "Kept pages are back under the original.");
        Assert.That(port.Original.Released, Is.False);
        Assert.That(swap.Blocked, Is.True);
        Assert.That(swap.Pending, Is.True);
        port.AssertNoLostTrees();

        swap.Run(port, Plan(port));
        Assert.That(port.Recorded.Format(), Is.EqualTo("A;S2x2x16;S4x4x16"));
        port.AssertNoLostTrees();
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Run_InvalidReplacementLinksOrLayout_RestoresOriginal(bool invalidLinks, bool ignoredLayout)
    {
        var port = new SwapPort { RejectNewLinks = invalidLinks, IgnoreNewLayout = ignoredLayout };
        var swap = new PageSetSwap<Pages>();

        Assert.Catch(new Action(() => swap.Run(port, Plan(port))));

        Assert.That(port.Current, Is.SameAs(port.Original));
        Assert.That(port.Recorded.Format(), Is.EqualTo(OriginalText));
        port.AssertNoLostTrees();
    }

    [TestCase("return_before")]
    [TestCase("return_after")]
    [TestCase("restore_before")]
    [TestCase("restore_detached")]
    [TestCase("restore_assigned")]
    [TestCase("restore_after")]
    [TestCase("verify_old_1")]
    [TestCase("write_old_before")]
    [TestCase("write_old_after")]
    [TestCase("release_new_before")]
    [TestCase("release_new_after")]
    public void Run_RollbackFailure_RetainsBothContainersUntilRetry(string rollbackFailure)
    {
        var port = new SwapPort("select_after", rollbackFailure);
        var swap = new PageSetSwap<Pages>();

        Assert.That(new Action(() => swap.Run(port, Plan(port))), Throws.TypeOf<AggregateException>());
        Assert.That(port.Original.Released, Is.False);
        Assert.That(swap.Pending, Is.True);
        Assert.That(swap.Blocked, Is.False);
        Assert.That(port.Allocated.Count, Is.EqualTo(2));

        swap.Run(port, PageCompactionPlanner.Plan(LayoutDescriptor.TryParse(OriginalText, out var layout) ? layout! : null!, OriginalCounts));
        Assert.That(port.Recorded.Format(), Is.EqualTo("A;S2x2x16;S4x4x16"));
        port.AssertNoLostTrees();
    }

    [TestCase("release_old_before")]
    [TestCase("release_old_after")]
    [TestCase("verify_new_3")]
    public void Run_CommittedCleanupFailure_RetriesCleanupWithoutReplacingCurrent(string failure)
    {
        var port = new SwapPort(failure);
        var swap = new PageSetSwap<Pages>();

        Assert.Catch(new Action(() => swap.Run(port, Plan(port))));
        var committed = port.Current;
        Assert.That(port.Recorded.Format(), Is.EqualTo("A;S2x2x16;S4x4x16"));
        Assert.That(swap.Blocked, Is.False);

        swap.Run(port, Plan(port));
        Assert.That(port.Current, Is.SameAs(committed));
        Assert.That(port.Allocated.Count, Is.EqualTo(2));
        Assert.That(swap.Pending, Is.False);
        port.AssertNoLostTrees();
    }

    [Test]
    public void Run_NonEmptyPageToRemoveOrUnsafeCell_ChangesNothing()
    {
        var notEmpty = new SwapPort();
        var plan = PageCompactionPlanner.Plan(notEmpty.RecordedLayout!, new[] { 0, 0, 5, 0, 2 });
        notEmpty.Original.EmptyPagesHoldRoots = true;
        Assert.Catch(new Action(() => new PageSetSwap<Pages>().Run(notEmpty, plan)));
        Assert.That(notEmpty.Mutations, Is.Zero);

        var decoding = new SwapPort { Allowed = false };
        Assert.Catch(new Action(() => new PageSetSwap<Pages>().Run(decoding, Plan(decoding))));
        Assert.That(decoding.Mutations, Is.Zero);
    }

    [TestCase(false, "A", 0)]
    [TestCase(true, "A;L", 0)]
    [TestCase(true, "A", 1)]
    public void IsAtAnchor_RequiresOneEmptyPageWithoutMutation(bool allowed, string layout, int anchorRoots)
    {
        var port = new SwapPort(layout, Enumerable.Repeat(0, layout.Split(';').Length).ToArray()) { Allowed = allowed };
        port.Original.AnchorRoots = anchorRoots;

        Assert.That(new PageSetSwap<Pages>().IsAtAnchor(port), Is.False);
        Assert.That(port.Mutations, Is.Zero);
    }

    private sealed class Pages
    {
        internal Pages(LayoutDescriptor layout, IReadOnlyList<int> kept, int roots)
        {
            Layout = layout;
            Kept = kept;
            Roots = roots;
        }

        internal LayoutDescriptor Layout { get; }
        internal IReadOnlyList<int> Kept { get; }
        internal int Roots { get; }
        internal int AnchorRoots;
        internal bool EmptyPagesHoldRoots;
        internal bool PagesMoved;
        internal bool Linked;
        internal bool RenderingAttached;
        internal bool Released;
        internal int Selected = -1;
    }

    private sealed class SwapPort : IPageSetSwapPort<Pages>
    {
        private readonly HashSet<string> _failures;
        private readonly Dictionary<Pages, int> _verifications = new();
        private readonly int[] _counts;
        private Pages? _current;

        internal SwapPort(params string[] failures) : this(OriginalText, OriginalCounts, failures) { }

        internal SwapPort(string layout, int[] counts, params string[] failures)
        {
            _failures = new HashSet<string>(failures);
            LayoutDescriptor.TryParse(layout, out var parsed);
            _counts = counts;
            Original = new Pages(parsed!, Enumerable.Range(0, counts.Length).ToArray(), counts.Sum())
            {
                Linked = true,
                RenderingAttached = true
            };
            _current = Original;
            Recorded = parsed!;
            Allocated.Add(Original);
        }

        internal Pages Original { get; }
        internal List<Pages> Allocated { get; } = new();
        internal LayoutDescriptor Recorded;
        internal bool Allowed = true;
        internal bool RejectNewLinks;
        internal bool IgnoreNewLayout;
        internal int Mutations;

        public bool CanOperate => Allowed;
        public Pages Current => _current ?? throw new InvalidOperationException("No current pages.");
        public LayoutDescriptor? RecordedLayout => Recorded;

        public IReadOnlyList<int> RootCounts() =>
            ReferenceEquals(_current, Original) ? _counts.Select((count, page) => page == 0 ? Original.AnchorRoots : count).ToArray()
                : Current.Kept.Select(page => _counts[page]).ToArray();

        public bool CanStart(PageCompaction plan) => Allowed && _current is { Released: false, Linked: true } &&
            Recorded.Equals(_current.Layout) && !_current.EmptyPagesHoldRoots;

        public void Prepare(IReadOnlyList<int> keep, Action<Pages> retain)
        {
            Mutations++;
            Hit("prepare_before");
            var layout = Original.Layout.Without(Enumerable.Range(0, Original.Layout.Pages.Count).Except(keep).ToArray());
            var replacement = new Pages(layout, keep.ToArray(), keep.Sum(page => _counts[page]));
            Allocated.Add(replacement);
            retain(replacement);
            Original.PagesMoved = true;
            Hit("prepare_after");
        }

        public void Attach(Pages pages)
        {
            Mutations++;
            var prefix = ReferenceEquals(pages, Original) ? "restore" : "attach";
            Hit(prefix + "_before");
            var previous = _current;
            _current = null;
            Hit(prefix + "_detached");
            if (previous != null) previous.RenderingAttached = false;
            _current = pages;
            Hit(prefix + "_assigned");
            pages.RenderingAttached = true;
            foreach (var allocated in Allocated) allocated.Linked = ReferenceEquals(allocated, pages);
            Hit(prefix + "_after");
        }

        public bool IsLinked(Pages pages, LayoutDescriptor layout)
        {
            _verifications.TryGetValue(pages, out var calls);
            _verifications[pages] = ++calls;
            Hit((ReferenceEquals(pages, Original) ? "verify_old_" : "verify_new_") + calls);
            return (!RejectNewLinks || ReferenceEquals(pages, Original)) && ReferenceEquals(pages, _current) &&
                pages.RenderingAttached && pages.Linked && !pages.Released && pages.Layout.Equals(layout) &&
                (!ReferenceEquals(pages, Original) || !pages.PagesMoved);
        }

        public void WriteLayout(LayoutDescriptor layout)
        {
            Mutations++;
            var prefix = layout.Equals(Original.Layout) ? "write_old" : "write_new";
            Hit(prefix + "_before");
            if (!IgnoreNewLayout || layout.Equals(Original.Layout)) Recorded = layout;
            Hit(prefix + "_after");
        }

        public void SelectFirst(Pages pages)
        {
            Mutations++;
            Hit("select_before");
            pages.Selected = 0;
            Hit("select_after");
        }

        public void ReturnPages(Pages original)
        {
            Mutations++;
            Hit("return_before");
            original.PagesMoved = false;
            Hit("return_after");
        }

        public void Release(Pages pages)
        {
            Mutations++;
            if (pages.Released) return;
            if (ReferenceEquals(pages, _current) || pages.Linked)
                throw new InvalidOperationException("Only detached pages can be released.");
            if (ReferenceEquals(pages, Original) && !pages.PagesMoved)
                throw new InvalidOperationException("Kept pages would be destroyed with the original.");
            var prefix = ReferenceEquals(pages, Original) ? "release_old" : "release_new";
            Hit(prefix + "_before");
            pages.RenderingAttached = false;
            pages.Released = true;
            Hit(prefix + "_after");
        }

        internal void AssertNoLostTrees()
        {
            Assert.That(Allocated.Where(pages => !ReferenceEquals(pages, _current)).All(pages => pages.Released), Is.True);
            Assert.That(Allocated.Where(pages => pages.Released).All(pages => !pages.RenderingAttached && !pages.Linked), Is.True);
            Assert.That(Current.Released, Is.False);
            Assert.That(Current.Roots, Is.EqualTo(_counts.Sum()), "No root is lost.");
        }

        private void Hit(string point)
        {
            if (_failures.Remove(point)) throw new InvalidOperationException(point);
        }
    }
}
