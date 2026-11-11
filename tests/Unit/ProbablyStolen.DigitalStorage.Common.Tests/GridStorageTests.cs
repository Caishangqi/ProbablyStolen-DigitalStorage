using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Queries;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class GridStorageTests
{
    [Test]
    public void PoweredGridWithController_IsOnline()
    {
        var storage = OnlineStorage(new StubCell(CellState.NotEmpty), new StubCell(CellState.Empty));

        Assert.That(storage.GetStatus(), Is.EqualTo(GridStorageStatus.Online));
        Assert.That(storage.IsAvailable(), Is.True);
    }

    [Test]
    public void MissingControllerOrPower_IsNotAvailable()
    {
        var cell = new StubCell(CellState.NotEmpty);
        var noController = new GridStorage(
            Grid(new[] { new GridNode(1, GridNodeKind.Drive, 2, 1, 14, new IStorageCell[] { cell }) }), true);
        var unpowered = new GridStorage(Grid(new[]
        {
            new GridNode(0, GridNodeKind.Controller, 3),
            new GridNode(1, GridNodeKind.Drive, 2, 1, 14, new IStorageCell[] { cell })
        }), false);

        Assert.That(noController.GetStatus(), Is.EqualTo(GridStorageStatus.NoController));
        Assert.That(noController.IsAvailable(), Is.False);
        Assert.That(unpowered.GetStatus(), Is.EqualTo(GridStorageStatus.Unpowered));
        Assert.That(unpowered.IsAvailable(), Is.False);
    }

    [Test]
    public void DuplicateCellReferenceInOneGrid_IsRejected()
    {
        var cell = new StubCell(CellState.NotEmpty);
        Assert.Throws<ArgumentException>(new Action(() => OnlineStorage(cell, cell)));
    }

    [Test]
    public void AbsentCell_ReportsStorageUnavailable()
    {
        var storage = OnlineStorage(new StubCell(CellState.NotEmpty), new StubCell(CellState.Absent));

        Assert.That(storage.GetStatus(), Is.EqualTo(GridStorageStatus.StorageUnavailable));
        Assert.That(storage.IsAvailable(), Is.False);
    }

    private static GridStorage OnlineStorage(params IStorageCell[] cells)
    {
        var nodes = new List<GridNode>
        {
            new(0, GridNodeKind.Controller, 3),
            new(1, GridNodeKind.Drive, 2, cells.Length, 14, cells),
            new(2, GridNodeKind.Terminal, 1)
        };
        return new GridStorage(Grid(nodes), true);
    }

    private static Grid Grid(IReadOnlyList<GridNode> nodes)
    {
        var edges = nodes.Count switch
        {
            2 => new[] { new GridConnection(nodes[0].Id, nodes[1].Id) },
            3 => new[]
            {
                new GridConnection(nodes[0].Id, nodes[1].Id),
                new GridConnection(nodes[1].Id, nodes[2].Id)
            },
            _ => Array.Empty<GridConnection>()
        };
        return new GridPropagator().Propagate(nodes, edges).Single();
    }

    private sealed class StubCell : IStorageCell
    {
        private readonly CellState _state;

        internal StubCell(CellState state) => _state = state;

        public CellState GetStatus() => _state;

        public bool IsPreferredStorageFor(ItemKey what, IActionSource source) => false;

        public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source) =>
            throw new NotSupportedException();

        public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode,
            IActionSource source) => throw new NotSupportedException();

        public void GetAvailableStacks(KeyCounter output) => throw new NotSupportedException();

        public string GetDescription() => "stub";
    }
}
