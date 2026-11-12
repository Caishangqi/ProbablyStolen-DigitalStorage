using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class ExternalAccessScopeTests
{
    private readonly GameItem _onlineCell = new("digital_storage.cell_1k");
    private readonly GameItem _offlineCell = new("digital_storage.cell_4k");
    private readonly GameItem _onlineRoot = new("item.apple");
    private readonly GameItem _offlineRoot = new("item.pear");
    private readonly GameItem _looseItem = new("item.plum");
    private readonly List<GameItem> _compacted = new();
    private readonly List<string> _logs = new();
    private int _onlineChecks;
    private int _frame = 1;
    private bool _mainThread = true;

    private ExternalAccessScope NewScope() => new(
        item => item == _onlineRoot ? _onlineCell : item == _offlineRoot ? _offlineCell : null,
        cell =>
        {
            _onlineChecks++;
            return cell == _onlineCell;
        },
        cell => _compacted.Add(cell),
        () => _frame,
        () => _mainThread,
        message => _logs.Add(message));

    private static IActionSource Source => ActionSource.OfMod("other_mod");

    [SetUp]
    public void ResetState()
    {
        // NUnit reuses one fixture instance, and scopes keep a static active instance: start every test clean.
        _compacted.Clear();
        _logs.Clear();
        _onlineChecks = 0;
        _mainThread = true;
        _frame += 10;
        using (NewScope().Open(Source))
        {
        }

        _compacted.Clear();
        _logs.Clear();
        _onlineChecks = 0;
    }

    [Test]
    public void Outside_A_Scope_NothingIsRemovable()
    {
        NewScope();

        Assert.That(ExternalAccessScope.AllowsRemoval(_onlineRoot), Is.False);
        Assert.That(ExternalAccessScope.AllowsRemoval(_offlineRoot), Is.False);
        Assert.That(ExternalAccessScope.AllowsRemoval(_looseItem), Is.False);
        Assert.That(_onlineChecks, Is.Zero);
    }

    [Test]
    public void Inside_A_Scope_OnlyRootsOfOnlineCellsAreRemovable()
    {
        var scope = NewScope();

        using (scope.Open(Source))
        {
            Assert.That(scope.IsOpen, Is.True);
            Assert.That(ExternalAccessScope.AllowsRemoval(_onlineRoot), Is.True);
            Assert.That(ExternalAccessScope.AllowsRemoval(_offlineRoot), Is.False);
            Assert.That(ExternalAccessScope.AllowsRemoval(_looseItem), Is.False, "Items outside cell pages are never ours.");
        }

        Assert.That(scope.IsOpen, Is.False);
        Assert.That(ExternalAccessScope.AllowsRemoval(_onlineRoot), Is.False);
    }

    [Test]
    public void OnlineState_IsCheckedOncePerCellAndScope()
    {
        var scope = NewScope();

        using (scope.Open(Source))
        {
            for (var index = 0; index < 5; index++) ExternalAccessScope.AllowsRemoval(_onlineRoot);
        }

        Assert.That(_onlineChecks, Is.EqualTo(1));
    }

    [Test]
    public void NestedScopes_CloseWithTheOutermost_AndCompactTouchedCellsOnce()
    {
        var scope = NewScope();
        var outer = scope.Open(Source);
        var inner = scope.Open(Source);
        scope.Touched(_onlineCell);
        scope.Touched(_onlineCell);

        inner.Dispose();
        inner.Dispose();
        Assert.That(scope.IsOpen, Is.True, "A repeated inner dispose must not close the outer scope.");
        Assert.That(_compacted, Is.Empty, "Compaction waits for the scope to close.");

        outer.Dispose();
        Assert.That(scope.IsOpen, Is.False);
        Assert.That(_compacted, Is.EqualTo(new[] { _onlineCell }));
    }

    [Test]
    public void Touched_OutsideAScope_CompactsAtOnce()
    {
        var scope = NewScope();

        scope.Touched(_offlineCell);

        Assert.That(_compacted, Is.EqualTo(new[] { _offlineCell }));
    }

    [Test]
    public void LeakedScope_IsResetOnTheNextFrame_AndItsOldHandleCannotCloseTheNewOne()
    {
        var scope = NewScope();
        var leaked = scope.Open(Source);
        scope.Touched(_onlineCell);

        _frame++;
        var fresh = scope.Open(Source);
        leaked.Dispose();

        Assert.That(scope.IsOpen, Is.True, "The leaked handle belongs to an older scope.");
        Assert.That(_compacted, Is.EqualTo(new[] { _onlineCell }), "Resetting closes the leaked scope.");
        Assert.That(_logs, Has.Some.Contains("never closed"));
        fresh.Dispose();
        Assert.That(scope.IsOpen, Is.False);
    }

    [Test]
    public void LeakedScope_DeniesRemovalOnALaterFrame()
    {
        var scope = NewScope();
        scope.Open(Source);
        Assert.That(ExternalAccessScope.AllowsRemoval(_onlineRoot), Is.True);

        _frame++;

        Assert.That(ExternalAccessScope.AllowsRemoval(_onlineRoot), Is.False);
        Assert.That(scope.IsOpen, Is.False);
        Assert.That(_logs, Has.Some.Contains("outlived"));
    }

    [Test]
    public void CellsOfAPassivelyClosedScope_AreCompactedWhenTheNextScopeOpens()
    {
        var scope = NewScope();
        scope.Open(Source);
        scope.Touched(_onlineCell);
        _frame++;
        ExternalAccessScope.AllowsRemoval(_onlineRoot);
        Assert.That(_compacted, Is.Empty, "No compaction inside a native removal check.");

        using (scope.Open(Source))
        {
            Assert.That(_compacted, Is.EqualTo(new[] { _onlineCell }));
        }
    }

    [Test]
    public void FailingOnlineCheck_DeniesAndLogs()
    {
        var scope = new ExternalAccessScope(_ => _onlineCell, _ => throw new InvalidOperationException("boom"),
            _ => { }, () => _frame, () => true, message => _logs.Add(message));

        using (scope.Open(Source))
        {
            Assert.That(ExternalAccessScope.AllowsRemoval(_onlineRoot), Is.False);
        }

        Assert.That(_logs, Has.Some.Contains("network check"));
    }

    [Test]
    public void FailingCompaction_IsLoggedAndOtherCellsStillCompact()
    {
        var compacted = new List<GameItem>();
        var scope = new ExternalAccessScope(_ => null, _ => true,
            cell =>
            {
                if (cell == _onlineCell) throw new InvalidOperationException("boom");
                compacted.Add(cell);
            },
            () => _frame, () => true, message => _logs.Add(message));

        using (scope.Open(Source))
        {
            scope.Touched(_onlineCell);
            scope.Touched(_offlineCell);
        }

        Assert.That(compacted, Is.EqualTo(new[] { _offlineCell }));
        Assert.That(_logs, Has.Some.Contains("compaction"));
    }

    [Test]
    public void Open_OffTheMainThread_IsRefused()
    {
        var scope = NewScope();
        _mainThread = false;

        Assert.Throws<InvalidOperationException>(new Action(() => scope.Open(Source)));
        Assert.That(scope.IsOpen, Is.False);
    }
}
