using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Machines;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class BayGroupShiftTests
{
    private static readonly bool[] DriveBays = Enumerable.Repeat(true, 14).ToArray();
    private static readonly bool[] NoneBlocked = new bool[14];

    [Test]
    public void Plan_ShiftsTheBlockOntoItsOwnBaysLeadingCellFirst() =>
        Assert.That(BayGroupShift.Plan(7, DriveBays, NoneBlocked, new[] { 0, 1 }, 0, 1),
            Is.EqualTo(new[] { (1, 2), (0, 1) }));

    [Test]
    public void Plan_ShiftsBackwardsTrailingCellFirst() =>
        Assert.That(BayGroupShift.Plan(7, DriveBays, NoneBlocked, new[] { 2, 3 }, 3, 2),
            Is.EqualTo(new[] { (2, 1), (3, 2) }));

    [Test]
    public void Plan_KeepsTheFormationAcrossRows() =>
        Assert.That(BayGroupShift.Plan(2, new[] { true, true, true, true, true, true }, new bool[6], new[] { 0, 2 }, 0, 1),
            Is.EquivalentTo(new[] { (0, 1), (2, 3) }));

    [Test]
    public void Plan_RefusesABlockThatWouldLeaveTheGrid()
    {
        Assert.That(BayGroupShift.Plan(7, DriveBays, NoneBlocked, new[] { 5, 6 }, 5, 6), Is.Null);
        Assert.That(BayGroupShift.Plan(7, DriveBays, NoneBlocked, new[] { 7, 8 }, 7, 6), Is.Null);
    }

    [Test]
    public void Plan_RefusesBaysHeldByOtherCellsOrMissing()
    {
        var blocked = new bool[14];
        blocked[2] = true;
        Assert.That(BayGroupShift.Plan(7, DriveBays, blocked, new[] { 0, 1 }, 0, 1), Is.Null);

        var bays = DriveBays.ToArray();
        bays[3] = false;
        Assert.That(BayGroupShift.Plan(7, bays, NoneBlocked, new[] { 0, 1 }, 0, 2), Is.Null);
    }

    [Test]
    public void Plan_DoesNothingWithoutAShift() =>
        Assert.That(BayGroupShift.Plan(7, DriveBays, NoneBlocked, new[] { 0, 1 }, 1, 1), Is.Null);

    [Test]
    public void Plan_RejectsAnAnchorOutsideTheGroup() =>
        Assert.Throws<ArgumentException>(new Action(() => BayGroupShift.Plan(7, DriveBays, NoneBlocked, new[] { 0, 1 }, 4, 5)));
}
