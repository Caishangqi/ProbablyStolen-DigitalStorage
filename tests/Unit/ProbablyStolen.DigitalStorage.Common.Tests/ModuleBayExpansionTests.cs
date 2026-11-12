using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class ModuleBayExpansionTests
{
    [Test]
    public void MaxStage_IsNoneForLowTiersOneForMiddleTiersAndTwoFor256K()
    {
        var stages = StorageCellTier.All.Select(ModuleBayExpansion.MaxStage).ToArray();

        Assert.That(stages, Is.EqualTo(new[] { 0, 0, 1, 1, 2 }));
    }

    [Test]
    public void CanExpand_StopsAtTheTierLimitAndFitsAllowsReachingIt()
    {
        var tier16K = StorageCellTier.All[2];

        Assert.That(ModuleBayExpansion.CanExpand(0, tier16K), Is.True);
        Assert.That(ModuleBayExpansion.CanExpand(1, tier16K), Is.False);
        Assert.That(ModuleBayExpansion.CanExpand(-1, tier16K), Is.False);
        Assert.That(ModuleBayExpansion.Fits(1, tier16K), Is.True);
        Assert.That(ModuleBayExpansion.Fits(2, tier16K), Is.False);
        Assert.That(ModuleBayExpansion.CanExpand(0, StorageCellTier.All[0]), Is.False);
    }

    [TestCase(0, 26)]
    [TestCase(1, 31)]
    [TestCase(2, 35)]
    public void Shape_RepeatsTheNativeSevenByFiveBay(int stage, int open)
    {
        var shape = ModuleBayExpansion.Shape(stage);

        Assert.That(shape.Length, Is.EqualTo(ModuleBayExpansion.Width * 5));
        Assert.That(shape.Count(c => c == '0'), Is.EqualTo(open));
        Assert.That(shape.All(c => c == '0' || c == '1'), Is.True);
    }

    [Test]
    public void Apply_RestoresThePriorStageWhenTheFinalValidationFails()
    {
        var calls = new System.Collections.Generic.List<string>();
        Assert.Throws<InvalidOperationException>(new Action(() => ModuleBayExpansion.Apply(0, StorageCellTier.All[2],
            (shape, _) => calls.Add("shape:" + shape), stage => calls.Add("stage:" + stage),
            () =>
            {
                calls.Add("validate");
                if (calls.Count == 3) throw new InvalidOperationException("synthetic validation failure");
            })));

        Assert.That(calls, Is.EqualTo(new[]
        {
            "shape:" + ModuleBayExpansion.Shape(1), "stage:1", "validate",
            "shape:" + ModuleBayExpansion.Shape(0), "stage:0", "validate"
        }));
    }

    [Test]
    public void Apply_PropagatesWhenTheStageWriteFailsAfterShapeChange()
    {
        var shapes = new System.Collections.Generic.List<string>();
        var stageWrites = 0;
        Assert.Throws<InvalidOperationException>(new Action(() => ModuleBayExpansion.Apply(0, StorageCellTier.All[2],
            (shape, _) => shapes.Add(shape), _ =>
            {
                stageWrites++;
                if (stageWrites == 1) throw new InvalidOperationException("synthetic stage failure");
            }, () => { })));

        Assert.That(shapes, Is.EqualTo(new[] { ModuleBayExpansion.Shape(1), ModuleBayExpansion.Shape(0) }));
    }

    [Test]
    public void Shape_MatchesTheNativeMachineConstants()
    {
        // Copied from the target build's MachineHelper: DEFAULT_MODULE_BAY_CONFIG and the two ExpandModule shapes.
        Assert.That(ModuleBayExpansion.Shape(0), Is.EqualTo("10000010001000001110000010001000001"));
        Assert.That(ModuleBayExpansion.Shape(1), Is.EqualTo("10000010000000000000000000001000001"));
        Assert.That(ModuleBayExpansion.Shape(2), Is.EqualTo(new string('0', 35)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => ModuleBayExpansion.Shape(3)));
    }
}
