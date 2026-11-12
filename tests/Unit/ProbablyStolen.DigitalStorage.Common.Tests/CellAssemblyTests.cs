using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Tests;

[TestFixture]
public sealed class CellAssemblyTests
{
    private static StorageCellDisassemblyRecipe Recipe() => new(new StorageComponent());

    [Test]
    public void Assembly_OriginalSingleComponentAndEmptyHousing_Accepts()
    {
        Assert.That(Recipe().CanAssemble(StorageCellDisassemblyRecipe.HousingId, 1,
            StorageComponent.ItemId, 1, 1, 2, false, false), Is.True);
    }

    [TestCase("other", 1, 1, 2, false, false)]
    [TestCase(StorageComponent.ItemId, 2, 1, 2, false, false)]
    [TestCase(StorageComponent.ItemId, 1, 2, 1, false, false)]
    [TestCase(StorageComponent.ItemId, 1, 1, 2, true, false)]
    [TestCase(StorageComponent.ItemId, 1, 1, 2, false, true)]
    public void Assembly_UnsupportedOrOccupiedInput_Rejects(string component, int count,
        int width, int height, bool occupied, bool mounted)
    {
        Assert.That(Recipe().CanAssemble(StorageCellDisassemblyRecipe.HousingId, 1,
            component, count, width, height, occupied, mounted), Is.False);
    }

    [TestCase(true, true, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public void Disassembly_StuckAndMountedCombinations_RequireUnlockedUnmounted(bool stuck, bool mounted, bool allowed)
    {
        var recipe = Recipe();

        Assert.That(recipe.CanDisassemble(StorageCellDisassemblyRecipe.CellId, 1,
            StorageComponent.ItemId, 1, stuck, mounted, storageEmpty: true), Is.EqualTo(allowed));
    }

    [TestCase(StorageCellDisassemblyRecipe.HousingId, StorageComponent.ItemId, 1)]
    [TestCase(StorageCellDisassemblyRecipe.CellId, null, 0)]
    [TestCase(StorageCellDisassemblyRecipe.CellId, "unknown", 1)]
    [TestCase(StorageCellDisassemblyRecipe.CellId, StorageComponent.ItemId, 2)]
    public void Disassembly_IncompleteOrUnknownGraph_Rejects(string cell, string? component, int count)
    {
        Assert.That(Recipe().CanDisassemble(cell, 1, component, count, false, false), Is.False);
    }

    [Test]
    public void Disassembly_RequiresExplicitEmptyEvidence()
    {
        var recipe = Recipe();

        Assert.That(recipe.CanDisassemble(StorageCellDisassemblyRecipe.CellId, 1,
            StorageComponent.ItemId, 1, false, false), Is.False);
        Assert.That(recipe.CanDisassemble(StorageCellDisassemblyRecipe.CellId, 1,
            StorageComponent.ItemId, 1, false, false, storageEmpty: true), Is.True);
    }

    [Test]
    public void Disassembly_RetainedContents_BlocksComponentRemovalUntilEmptied()
    {
        var recipe = Recipe();

        Assert.That(recipe.CanDisassemble(StorageCellDisassemblyRecipe.CellId, 1,
            StorageComponent.ItemId, 1, false, false, storageEmpty: false), Is.False);
        Assert.That(recipe.CanDisassemble(StorageCellDisassemblyRecipe.CellId, 1,
            StorageComponent.ItemId, 1, false, false, storageEmpty: true), Is.True);
    }

    [TestCase(StorageContentsState.Unavailable)]
    [TestCase(StorageContentsState.RecoveryRequired)]
    [TestCase((StorageContentsState)99)]
    public void Disassembly_UnknownOrRecoveryState_BlocksComponentRemoval(StorageContentsState state)
    {
        var callbacks = new CellAssemblyCallbacks(Recipe(), new AssemblyPort(state));

        Assert.That(callbacks.CanRemove(), Is.False);
        Assert.That(callbacks.GetRemovalFailure(), Is.EqualTo(CellRemovalFailure.STORAGE_STATE_UNAVAILABLE));
    }

    [Test]
    public void Disassembly_EmptyCellWithModulesOrRules_IsRefusedWithDistinctReasons()
    {
        var withModules = new CellAssemblyCallbacks(Recipe(), new SnapshotPort(() => Cell(StorageContentsState.Empty, hasModules: true)));
        var withRules = new CellAssemblyCallbacks(Recipe(), new SnapshotPort(() => Cell(StorageContentsState.Empty, hasRules: true)));
        var nonEmpty = new CellAssemblyCallbacks(Recipe(),
            new SnapshotPort(() => Cell(StorageContentsState.NonEmpty, hasModules: true, hasRules: true)));
        var clean = new CellAssemblyCallbacks(Recipe(), new SnapshotPort(() => Cell(StorageContentsState.Empty)));

        Assert.That(withModules.GetRemovalFailure(), Is.EqualTo(CellRemovalFailure.HAS_MODULES));
        Assert.That(withRules.GetRemovalFailure(), Is.EqualTo(CellRemovalFailure.HAS_PARTITION_RULES));
        Assert.That(nonEmpty.GetRemovalFailure(), Is.EqualTo(CellRemovalFailure.STORAGE_CELL_NOT_EMPTY));
        Assert.That(clean.CanRemove(), Is.True);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Disassembly_RecipeRefusesModulesOrRules(bool hasModules, bool hasRules)
    {
        Assert.That(Recipe().CanDisassemble(StorageCellDisassemblyRecipe.CellId, 1, StorageComponent.ItemId, 1,
            false, false, storageEmpty: true, hasModules: hasModules, hasRules: hasRules), Is.False);
    }

    [TestCase(0, 0, true)]
    [TestCase(1, 0, false)]
    [TestCase(1, 2, true)]
    [TestCase(2, 3, false)]
    [TestCase(2, 4, true)]
    public void Assembly_HousingExpandedBeyondTheComponentTier_IsRefused(int stage, int tierIndex, bool allowed)
    {
        var tier = StorageCellTier.All[tierIndex];
        var component = new PhysicalItemSnapshot(tier.ComponentId, 1, 1, 2);
        var callbacks = new CellAssemblyCallbacks(Recipe(), new SnapshotPort(() => new CellAssemblySnapshot(
            new PhysicalItemSnapshot(StorageCellDisassemblyRecipe.HousingId, 1, 1, 2), null,
            storageState: StorageContentsState.Empty, expansionStage: stage)));

        Assert.That(callbacks.StageExceedsTier(component), Is.EqualTo(!allowed));
        Assert.That(callbacks.CanInsert(component, false, true), Is.EqualTo(allowed));
    }

    private static CellAssemblySnapshot Cell(StorageContentsState state, bool hasModules = false, bool hasRules = false) => new(
        new PhysicalItemSnapshot(StorageCellDisassemblyRecipe.CellId, 1, 1, 2),
        new PhysicalItemSnapshot(StorageComponent.ItemId, 1, 1, 2),
        storageState: state, hasModules: hasModules, hasPartitionRules: hasRules);

    private sealed class SnapshotPort : ICellAssemblyPort
    {
        private readonly Func<CellAssemblySnapshot> _read;

        internal SnapshotPort(Func<CellAssemblySnapshot> read) => _read = read;

        public CellAssemblySnapshot Read() => _read();

        public void FixComponent() { }
        public void Present(bool assembled) { }
    }

    private sealed class AssemblyPort : ICellAssemblyPort
    {
        private readonly StorageContentsState _storageState;

        internal AssemblyPort(StorageContentsState storageState) => _storageState = storageState;

        public CellAssemblySnapshot Read() => new(
            new PhysicalItemSnapshot(StorageCellDisassemblyRecipe.CellId, 1, 1, 2),
            new PhysicalItemSnapshot(StorageComponent.ItemId, 1, 1, 2),
            storageState: _storageState);

        public void FixComponent() { }
        public void Present(bool assembled) { }
    }

    [Test]
    public void Bays_AllFourteenSlots_AcceptOnlyCompleteSingleCells()
    {
        var inventory = new DriveCellInventory(Recipe());

        Assert.That(inventory.Size(), Is.EqualTo(14));
        for (var slot = 0; slot < inventory.Size(); slot++)
        {
            Assert.That(inventory.GetSlotLimit(slot), Is.EqualTo(1));
            Assert.That(inventory.IsItemValid(slot, StorageCellDisassemblyRecipe.CellId,
                1, 1, 2, StorageComponent.ItemId, 1), Is.True);
        }
    }

    [TestCase(-1)]
    [TestCase(14)]
    [TestCase(int.MaxValue)]
    public void Bays_InvalidIndex_RejectsAndLimitThrows(int slot)
    {
        var inventory = new DriveCellInventory(Recipe());

        Assert.That(inventory.IsItemValid(slot, StorageCellDisassemblyRecipe.CellId,
            1, 1, 2, StorageComponent.ItemId, 1), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => inventory.GetSlotLimit(slot)));
    }

    [TestCase(StorageCellDisassemblyRecipe.HousingId, 1, 1, 2, StorageComponent.ItemId, 1)]
    [TestCase(StorageComponent.ItemId, 1, 1, 2, null, 0)]
    [TestCase(StorageCellDisassemblyRecipe.CellId, 2, 1, 2, StorageComponent.ItemId, 1)]
    [TestCase(StorageCellDisassemblyRecipe.CellId, 1, 2, 2, StorageComponent.ItemId, 1)]
    [TestCase(StorageCellDisassemblyRecipe.CellId, 1, 1, 2, null, 0)]
    public void Bays_InvalidIdentityCountShapeOrComponent_Rejects(string cell, int count,
        int width, int height, string? component, int componentCount)
    {
        var inventory = new DriveCellInventory(Recipe());

        Assert.That(inventory.IsItemValid(0, cell, count, width, height, component, componentCount), Is.False);
    }
}
