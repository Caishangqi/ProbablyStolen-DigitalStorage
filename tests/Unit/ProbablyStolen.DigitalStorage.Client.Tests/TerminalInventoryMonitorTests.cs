using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class TerminalInventoryMonitorTests
{
    [Test]
    public void DirtyMark_IsConsumedOnceAndDetectsCountAndAvailabilityChanges()
    {
        var monitor = new TerminalInventoryMonitor();
        var online = Snapshot(true, 5);
        Assert.That(monitor.BeginRefresh(1f, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
        Assert.That(monitor.HasChanged(online), Is.True);
        monitor.Accept(online);
        Assert.That(monitor.BeginRefresh(1.1f, true, false), Is.EqualTo(TerminalRefreshReason.None));

        monitor.MarkDirty();
        monitor.MarkDirty();
        Assert.That(monitor.BeginRefresh(1.2f, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
        Assert.That(monitor.BeginRefresh(1.3f, true, false), Is.EqualTo(TerminalRefreshReason.None));
        Assert.That(monitor.HasChanged(Snapshot(true, 5)), Is.False);
        Assert.That(monitor.HasChanged(Snapshot(true, 4)), Is.True);
        Assert.That(monitor.HasChanged(Snapshot(false, 0)), Is.True);
    }

    [Test]
    public void WithoutAudit_NoTimedRefreshOccurs()
    {
        var monitor = new TerminalInventoryMonitor(5f);
        Assert.That(monitor.BeginRefresh(0f, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
        Assert.That(monitor.BeginRefresh(1000f, true, false), Is.EqualTo(TerminalRefreshReason.None));
    }

    [Test]
    public void Audit_RunsAtItsIntervalOnlyWhenEnabled()
    {
        var monitor = new TerminalInventoryMonitor(5f);
        Assert.That(monitor.BeginRefresh(0f, true, true), Is.EqualTo(TerminalRefreshReason.Dirty));
        Assert.That(monitor.BeginRefresh(4.9f, true, true), Is.EqualTo(TerminalRefreshReason.None));
        Assert.That(monitor.BeginRefresh(5f, true, true), Is.EqualTo(TerminalRefreshReason.Audit));
        Assert.That(monitor.BeginRefresh(9.9f, true, true), Is.EqualTo(TerminalRefreshReason.None));

        monitor.MarkDirty();
        Assert.That(monitor.BeginRefresh(10f, true, true), Is.EqualTo(TerminalRefreshReason.Dirty));
        Assert.That(monitor.BeginRefresh(14.9f, true, true), Is.EqualTo(TerminalRefreshReason.None));
    }

    [Test]
    public void InactiveWindow_KeepsTheDirtyMarkForLater()
    {
        var monitor = new TerminalInventoryMonitor();
        Assert.That(monitor.BeginRefresh(0f, false, true), Is.EqualTo(TerminalRefreshReason.None));
        Assert.That(monitor.BeginRefresh(float.NaN, true, true), Is.EqualTo(TerminalRefreshReason.None));
        Assert.That(monitor.BeginRefresh(1f, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
    }

    [Test]
    public void ResetForReopen_RequiresAnImmediateAuthoritativeSnapshot()
    {
        var monitor = new TerminalInventoryMonitor();
        monitor.BeginRefresh(0f, true, false);
        monitor.Accept(Snapshot(true, 5));
        monitor.Reset(4f);
        Assert.That(monitor.BeginRefresh(4f, true, false), Is.EqualTo(TerminalRefreshReason.Dirty));
        Assert.That(monitor.HasChanged(Snapshot(true, 5)), Is.True);
    }

    [Test]
    public void Accept_SatisfiesAPendingDirtyMark()
    {
        var monitor = new TerminalInventoryMonitor();
        monitor.Reset(0f);
        monitor.Accept(Snapshot(true, 5));
        Assert.That(monitor.BeginRefresh(0f, true, false), Is.EqualTo(TerminalRefreshReason.None));
    }

    [Test]
    public void Constructor_RejectsNonPositiveOrNonFiniteIntervals()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>((System.Action)(() => _ = new TerminalInventoryMonitor(0f)));
        Assert.Throws<System.ArgumentOutOfRangeException>((System.Action)(() => _ = new TerminalInventoryMonitor(float.PositiveInfinity)));
    }

    private static TerminalInventorySnapshot Snapshot(bool online, long amount) => new(online,
        online && amount > 0 ? new[]
        {
            new TerminalEntry(new ItemKey("item.apple"), amount, "Apple", "items|apple")
        } : System.Array.Empty<TerminalEntry>());
}
