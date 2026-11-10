using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class EmptyPageResetTests
{
    [Test]
    public void Run_CompleteEmptyPages_CommitsOnePageAndReleasesOriginal()
    {
        var port = new ResetPort();
        var reset = new EmptyPageReset<Pages>();
        reset.Request();
        reset.Run(port);

        Assert.That(port.Current.Count, Is.EqualTo(1));
        Assert.That(port.RecordedCount, Is.EqualTo(1));
        Assert.That(port.Original.Released, Is.True);
        Assert.That(port.Current.Selected, Is.EqualTo(0));
        Assert.That(reset.Pending, Is.False);
        Assert.That(reset.CanAssemble(port), Is.True);
        port.AssertNoLostTrees();
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
    public void Run_ReplacementFailure_RestoresOriginalAndCanRetry(string failure)
    {
        var port = new ResetPort(failure);
        var reset = new EmptyPageReset<Pages>();

        Assert.Catch(new Action(() => reset.Run(port)));
        Assert.That(port.Current, Is.SameAs(port.Original));
        Assert.That(port.RecordedCount, Is.EqualTo(4));
        Assert.That(port.Original.Released, Is.False);
        Assert.That(reset.Pending, Is.True);
        Assert.That(reset.CanAssemble(port), Is.False);
        port.AssertNoLostTrees();

        reset.Run(port);
        Assert.That(reset.CanAssemble(port), Is.True);
        port.AssertNoLostTrees();
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Run_InvalidReplacementLinksOrCount_RestoresWithoutDiscardingOriginal(bool invalidLinks, bool ignoredCount)
    {
        var port = new ResetPort { RejectNewLinks = invalidLinks, IgnoreNewCount = ignoredCount };
        var reset = new EmptyPageReset<Pages>();

        Assert.Catch(new Action(() => reset.Run(port)));
        Assert.That(port.Current, Is.SameAs(port.Original));
        Assert.That(port.RecordedCount, Is.EqualTo(4));
        Assert.That(reset.CanAssemble(port), Is.False);
        port.AssertNoLostTrees();

        port.RejectNewLinks = false;
        port.IgnoreNewCount = false;
        reset.Run(port);
        Assert.That(reset.CanAssemble(port), Is.True);
        port.AssertNoLostTrees();
    }

    [TestCase("restore_before")]
    [TestCase("restore_detached")]
    [TestCase("restore_assigned")]
    [TestCase("restore_after")]
    [TestCase("verify_old_1")]
    [TestCase("write_old_before")]
    [TestCase("write_old_after")]
    [TestCase("release_new_before")]
    [TestCase("release_new_after")]
    public void Run_RollbackFailure_RetainsBothTreesUntilRetry(string rollbackFailure)
    {
        var port = new ResetPort("select_after", rollbackFailure);
        var reset = new EmptyPageReset<Pages>();

        Assert.That(new Action(() => reset.Run(port)), Throws.TypeOf<AggregateException>());
        Assert.That(port.Original.Released, Is.False);
        Assert.That(reset.Pending, Is.True);
        Assert.That(reset.CanAssemble(port), Is.False);
        Assert.That(port.Allocated.Count, Is.EqualTo(2));

        reset.Run(port);
        Assert.That(port.Allocated.Count, Is.EqualTo(3));
        Assert.That(reset.CanAssemble(port), Is.True);
        port.AssertNoLostTrees();
    }

    [TestCase("release_old_before")]
    [TestCase("release_old_after")]
    [TestCase("verify_new_3")]
    public void Run_CommittedCleanupFailure_RetriesCleanupWithoutReplacingCurrent(string failure)
    {
        var port = new ResetPort(failure);
        var reset = new EmptyPageReset<Pages>();

        Assert.Catch(new Action(() => reset.Run(port)));
        var committed = port.Current;
        Assert.That(committed.Count, Is.EqualTo(1));
        Assert.That(port.RecordedCount, Is.EqualTo(1));
        Assert.That(reset.CanAssemble(port), Is.False);

        reset.Run(port);
        Assert.That(port.Current, Is.SameAs(committed));
        Assert.That(port.Allocated.Count, Is.EqualTo(2));
        Assert.That(reset.CanAssemble(port), Is.True);
        port.AssertNoLostTrees();
    }

    [Test]
    public void Run_OrphanRenderingAfterFailedRollback_DetachesBeforeReleaseOnRetry()
    {
        var port = new ResetPort("select_after", "restore_detached", "detach_orphan_after");
        var reset = new EmptyPageReset<Pages>();

        Assert.Catch(new Action(() => reset.Run(port)));
        Assert.That(port.Allocated[1].RenderingAttached, Is.True);
        Assert.Catch(new Action(() => reset.Run(port)));
        Assert.That(port.Allocated[1].RenderingAttached, Is.False);
        Assert.That(port.Allocated[1].Released, Is.False);
        Assert.That(reset.CanAssemble(port), Is.False);

        reset.Run(port);
        Assert.That(reset.CanAssemble(port), Is.True);
        port.AssertNoLostTrees();
    }

    [TestCase(false, true, true, 1)]
    [TestCase(true, false, true, 1)]
    [TestCase(true, true, false, 1)]
    [TestCase(true, true, true, 4)]
    public void CanAssemble_UnsafeOrMultiplePageHousing_RejectsWithoutMutation(
        bool canOperate, bool complete, bool empty, int count)
    {
        var port = new ResetPort(count: count) { Allowed = canOperate, Complete = complete };
        port.Current.Empty = empty;
        var reset = new EmptyPageReset<Pages>();

        Assert.That(reset.CanAssemble(port), Is.False);
        Assert.That(port.Mutations, Is.EqualTo(0));
        Assert.That(port.Current.Released, Is.False);
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void Run_DecodingIncompleteOrNonemptyHousing_PreservesOriginal(
        bool canOperate, bool complete, bool empty)
    {
        var port = new ResetPort { Allowed = canOperate, Complete = complete };
        port.Current.Empty = empty;
        var reset = new EmptyPageReset<Pages>();

        Assert.Catch(new Action(() => reset.Run(port)));
        Assert.That(port.Mutations, Is.EqualTo(0));
        Assert.That(port.Current, Is.SameAs(port.Original));
        Assert.That(port.Current.Empty, Is.EqualTo(empty));
        Assert.That(reset.Pending, Is.True);
    }

    [Test]
    public void Run_CleanupFindsChangedOriginal_RetainsItWithoutDestroyingItems()
    {
        var port = new ResetPort("release_old_before");
        var reset = new EmptyPageReset<Pages>();
        Assert.Catch(new Action(() => reset.Run(port)));
        port.Original.Empty = false;

        Assert.Catch(new Action(() => reset.Run(port)));
        Assert.That(port.Original.Released, Is.False);
        Assert.That(reset.CanAssemble(port), Is.False);
        Assert.That(port.Allocated.Count, Is.EqualTo(2));
    }

    private sealed class Pages
    {
        internal Pages(int count) => Count = count;
        internal int Count { get; }
        internal bool Empty = true;
        internal bool Linked;
        internal bool RenderingAttached;
        internal bool Released;
        internal int Selected = -1;
    }

    private sealed class ResetPort : IEmptyPageResetPort<Pages>
    {
        private readonly HashSet<string> _failures;
        private readonly Dictionary<Pages, int> _verifications = new();
        private Pages? _current;
        internal ResetPort(params string[] failures) : this(failures, 4) { }
        internal ResetPort(int count) : this(Array.Empty<string>(), count) { }
        private ResetPort(string[] failures, int count)
        {
            _failures = new HashSet<string>(failures);
            Original = new Pages(count) { Linked = true, RenderingAttached = true };
            _current = Original;
            RecordedCount = count;
            Allocated.Add(Original);
        }

        internal Pages Original { get; }
        internal List<Pages> Allocated { get; } = new();
        internal bool Allowed = true;
        internal bool Complete = true;
        internal bool RejectNewLinks;
        internal bool IgnoreNewCount;
        internal int RecordedCount;
        internal int Mutations;
        public bool CanOperate => Allowed;
        public bool CanStart => Allowed && Complete && _current is { Empty: true, Released: false, Linked: true } &&
            RecordedCount == _current.Count;
        public Pages Current => _current ?? throw new InvalidOperationException("No current page set.");
        public int Count(Pages pages) => pages.Count;

        public void Prepare(Action<Pages> retain)
        {
            Mutations++;
            Hit("prepare_before");
            var replacement = new Pages(1);
            Allocated.Add(replacement);
            retain(replacement);
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

        public bool IsLinkedEmpty(Pages pages, int count)
        {
            _verifications.TryGetValue(pages, out var calls);
            _verifications[pages] = ++calls;
            Hit((ReferenceEquals(pages, Original) ? "verify_old_" : "verify_new_") + calls);
            return (!RejectNewLinks || ReferenceEquals(pages, Original)) && ReferenceEquals(pages, _current) &&
                pages.RenderingAttached && pages.Linked && !pages.Released && pages.Empty && pages.Count == count;
        }

        public bool HasRecordedCount(int count) => RecordedCount == count;
        public void WriteCount(int count)
        {
            Mutations++;
            var prefix = count == Original.Count ? "write_old" : "write_new";
            Hit(prefix + "_before");
            if (!IgnoreNewCount || count == Original.Count) RecordedCount = count;
            Hit(prefix + "_after");
        }

        public void SelectFirst(Pages pages)
        {
            Mutations++;
            Hit("select_before");
            pages.Selected = 0;
            Hit("select_after");
        }

        public void Release(Pages pages)
        {
            Mutations++;
            if (pages.Released) return;
            if (ReferenceEquals(pages, _current) || pages.Linked || !pages.Empty)
                throw new InvalidOperationException("Only detached empty pages can be released.");
            if (pages.RenderingAttached)
            {
                Hit("detach_orphan_before");
                pages.RenderingAttached = false;
                Hit("detach_orphan_after");
            }
            var prefix = ReferenceEquals(pages, Original) ? "release_old" : "release_new";
            Hit(prefix + "_before");
            pages.Released = true;
            Hit(prefix + "_after");
        }

        internal void AssertNoLostTrees()
        {
            Assert.That(Allocated.Where(pages => !ReferenceEquals(pages, _current)).All(pages => pages.Released), Is.True);
            Assert.That(Allocated.Where(pages => pages.Released).All(pages => !pages.RenderingAttached && !pages.Linked), Is.True);
            Assert.That(Current.Released, Is.False);
        }

        private void Hit(string point)
        {
            if (_failures.Remove(point)) throw new InvalidOperationException(point);
        }
    }
}
