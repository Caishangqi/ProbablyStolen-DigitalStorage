using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;

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
