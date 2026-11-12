using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellRecordTests
{
    [Test]
    public void Schema_DefaultsToOneWithoutTheTag()
    {
        Assert.That(CellRecord.Schema(false, 0), Is.EqualTo(1));
        Assert.That(CellRecord.Schema(true, 2), Is.EqualTo(2));
        Assert.That(CellRecord.Schema(true, 0), Is.Zero, "A corrupt value is kept for the audit to reject.");
    }

    [Test]
    public void Layout_FromPageCountWithoutADescriptor()
    {
        Assert.That(CellRecord.TryLayout(null, false, 0, out var single), Is.True);
        Assert.That(single!.Format(), Is.EqualTo("A"));
        Assert.That(CellRecord.TryLayout(null, true, 16, out var sixteen), Is.True);
        Assert.That(sixteen!.Format(), Is.EqualTo("A;L*15"));
        Assert.That(CellRecord.TryLayout(null, true, 0, out _), Is.False);
        Assert.That(CellRecord.TryLayout(null, true, 32769, out _), Is.False);
    }

    [Test]
    public void Layout_DescriptorWinsOverTheDowngradeGuard()
    {
        Assert.That(CellRecord.TryLayout("A;S1x1x16", true, CellRecord.DowngradeGuardPageCount, out var layout), Is.True);
        Assert.That(layout!.Pages.Count, Is.EqualTo(2));
        Assert.That(CellRecord.TryLayout("2|A", true, 1, out _), Is.False, "An invalid descriptor never falls back.");
    }

    [Test]
    public void MidMigrationCell_IsStillPendingMigration()
    {
        // Migration writes the descriptor first and the schema last; a stop in between leaves schema 1.
        var schema = CellRecord.Schema(false, 0);
        Assert.That(CellRecord.TryLayout("A;L*2;S2x2x16", true, CellRecord.DowngradeGuardPageCount, out var layout), Is.True);

        Assert.That(CellSchema.Classify(schema), Is.EqualTo(SchemaGate.Migrate));
        Assert.That(layout!.Pages.Count(page => page.Kind == PageKind.Legacy), Is.EqualTo(2));
    }

    [Test]
    public void PartitionMode_ReadsBothModesAndRejectsUnknownValues()
    {
        Assert.That(CellRecord.PartitionMode(null), Is.EqualTo(PartitionMode.Whitelist), "No tag is the default whitelist.");
        Assert.That(CellRecord.PartitionMode("none"), Is.EqualTo(PartitionMode.Whitelist),
            "The removed unrestricted mode reads as a whitelist.");
        Assert.That(CellRecord.PartitionMode("whitelist"), Is.EqualTo(PartitionMode.Whitelist));
        Assert.That(CellRecord.PartitionMode("blacklist"), Is.EqualTo(PartitionMode.Blacklist));
        foreach (var mode in new[] { PartitionMode.Whitelist, PartitionMode.Blacklist })
            Assert.That(CellRecord.PartitionMode(CellRecord.PartitionModeText(mode)), Is.EqualTo(mode));
        Assert.That(CellRecord.PartitionMode("Blacklist"), Is.Null);
        Assert.That(CellRecord.PartitionMode(""), Is.Null);
    }

    [Test]
    public void RestorePlan_AppendsEveryRecordedPageAfterTheFactoryPage()
    {
        LayoutDescriptor.TryParse("A;L;S3x1x16", out var layout);

        var plan = CellRecord.RestorePlan(1, layout, 1)!;

        Assert.That(plan, Is.EqualTo(new[] { PageGeometry.Legacy, StripLayout.PageFor(new BoundingSize(3, 1)) }));
        Assert.That(CellRecord.RestorePlan(2, layout, 3), Is.Empty);
    }

    [Test]
    public void RestorePlan_RestoresLockedCellsButNotNewerOrBrokenOnes()
    {
        LayoutDescriptor.TryParse("A;L", out var layout);

        Assert.That(CellSchema.Classify(1, 3), Is.EqualTo(SchemaGate.TooOld));
        Assert.That(CellRecord.RestorePlan(CellSchema.Current + 1, layout, 1), Is.Null);
        Assert.That(CellRecord.RestorePlan(0, layout, 1), Is.Null);
        Assert.That(CellRecord.RestorePlan(2, null, 1), Is.Null);
        Assert.That(CellRecord.RestorePlan(2, layout, 3), Is.Null, "More pages than recorded is not a restore.");
    }

    [Test]
    public void Health_AfterLoadFollowsSchemaLayoutAndPages()
    {
        Assert.That(CellHealthState.AfterLoad(SchemaGate.Current, true, true).Status, Is.EqualTo(CellHealthStatus.Available));
        Assert.That(CellHealthState.AfterLoad(SchemaGate.Migrate, true, true).Status, Is.EqualTo(CellHealthStatus.Available));
        var locked = CellHealthState.AfterLoad(SchemaGate.TooOld, true, true);
        Assert.That(locked.Status, Is.EqualTo(CellHealthStatus.Locked));
        Assert.That(locked.RisksItemLoss, Is.False);
        Assert.That(locked.IsMountable, Is.False);
        var tooNew = CellHealthState.AfterLoad(SchemaGate.TooNew, true, true);
        Assert.That((tooNew.Reason, tooNew.RisksItemLoss), Is.EqualTo((CellUnavailableReason.SchemaTooNew, true)));
        Assert.That(CellHealthState.AfterLoad(SchemaGate.Invalid, true, true).Reason, Is.EqualTo(CellUnavailableReason.SchemaTooNew));
        Assert.That(CellHealthState.AfterLoad(SchemaGate.Current, false, true).Reason, Is.EqualTo(CellUnavailableReason.InvalidLayout));
        Assert.That(CellHealthState.AfterLoad(SchemaGate.Current, true, false).Reason, Is.EqualTo(CellUnavailableReason.RestoreFailed));
        Assert.That(new CellHealthState(CellHealthStatus.ReadOnly).IsMountable, Is.True);
    }

    [Test]
    public void Lore_ShowsModulesExpansionAndAnInvalidPartition()
    {
        var modules = new ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades.UpgradeInventory(new[]
        {
            ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades.InstalledModule.TypeLimit(
                new ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades.TypeLimitRoll(
                    ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades.ModuleQuality.Common, 8, 5)),
            ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades.InstalledModule.EqualDistribution()
        });
        var view = new CellView(CellRules.For(StorageCellTier.All[2]), 142,
            new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }, new CellUsage(),
            CellPartition.Invalid(PartitionMode.Whitelist, CellPartitionFailure.MalformedRule), upgrades: modules);

        var lines = CellLore.Lines(new CellLoreInput { View = view, ExpansionStage = 1 });

        Assert.That(lines.Select(line => line.ToString()), Is.SupersetOf(new[]
        {
            "cell_storage_partition_invalid()", "cell_storage_type_modules(1)", "cell_storage_equal_distribution()",
            "cell_storage_expansion(1,1)"
        }));
    }

    [Test]
    public void Lore_ListsBytesTypesPartitionSchemaAndStatusInOrder()
    {
        var partition = Partitions.Blacklist(new ItemKey("item.a"));
        var view = new CellView(CellRules.For(StorageCellTier.All[0]), 128,
            new[] { PageView.Unslotted(PageGeometry.Anchor, 0) }, new CellUsage(), partition);
        var lines = CellLore.Lines(new CellLoreInput
        {
            Schema = 1,
            View = view,
            Health = new CellHealthState(CellHealthStatus.ReadOnly)
        });

        Assert.That(lines.Select(line => line.Key), Is.EqualTo(new[]
        {
            "cell_storage_bytes", "cell_storage_types", "cell_storage_partition_exclude", "cell_storage_schema",
            "cell_storage_migration_refused"
        }));
        Assert.That(lines[0].Arguments, Is.EqualTo(new object[] { 0L, 1024L }));
        Assert.That(lines[3].Arguments, Is.EqualTo(new object[] { 1, 2 }));
    }

    [TestCase(CellHealthStatus.Available, CellUnavailableReason.None, null)]
    [TestCase(CellHealthStatus.CompactionBlocked, CellUnavailableReason.None, "cell_storage_compaction_blocked")]
    [TestCase(CellHealthStatus.Locked, CellUnavailableReason.None, "cell_storage_locked")]
    [TestCase(CellHealthStatus.Unavailable, CellUnavailableReason.SchemaTooNew, "cell_storage_schema_too_new")]
    [TestCase(CellHealthStatus.Unavailable, CellUnavailableReason.MigrationFailed, "cell_storage_migration_failed")]
    [TestCase(CellHealthStatus.Unavailable, CellUnavailableReason.RollbackFailed, "cell_storage_rollback_failed")]
    [TestCase(CellHealthStatus.Unavailable, CellUnavailableReason.RestoreFailed, "cell_storage_unavailable")]
    public void Lore_StatusLineForEachState(CellHealthStatus status, CellUnavailableReason reason, string? expected)
    {
        var lines = CellLore.Lines(new CellLoreInput { Schema = 1, Health = new CellHealthState(status, reason) });

        Assert.That(lines[0].Key, Is.EqualTo("cell_storage_schema"), "Unreadable cells still show their format.");
        Assert.That(lines.Count > 1 ? lines[1].Key : null, Is.EqualTo(expected));
        if (status == CellHealthStatus.Locked) Assert.That(lines[1].Arguments, Is.EqualTo(new object[] { CellSchema.UpgradePath[1] }));
    }
}
