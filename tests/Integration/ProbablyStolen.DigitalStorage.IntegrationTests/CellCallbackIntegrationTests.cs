using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class CellCallbackIntegrationTests
{
    [Test]
    public void AssemblyAndExtraction_OriginalReferencesAndMetadata_ConservePhysicalItems()
    {
        var source = new PhysicalTestSlot();
        var destination = new PhysicalTestSlot();
        var original = new PhysicalTestItem();
        source.TransferHere(original);
        var cell = new NativeCellBoundary();
        var host = cell.Host;
        var metadata = original.Metadata;

        Assert.That(cell.Insert(original), Is.True);
        Assert.That(source.Item, Is.Null);
        Assert.That(cell.Component.Item, Is.SameAs(original));
        Assert.That(original.Parent, Is.SameAs(cell.Component));
        Assert.That(original.Count, Is.EqualTo(1));
        Assert.That(original.Fixed, Is.True);
        Assert.That(host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.CellId));

        Assert.That(cell.Extract(destination), Is.False);
        original.Fixed = false;
        Assert.That(cell.Extract(destination), Is.True);
        Assert.That(destination.Item, Is.SameAs(original));
        Assert.That(cell.Component.Item, Is.Null);
        Assert.That(original.Parent, Is.SameAs(destination));
        Assert.That(original.Metadata, Is.EqualTo(metadata));
        Assert.That(original.Count, Is.EqualTo(1));
        Assert.That(cell.Host, Is.SameAs(host));
        Assert.That(cell.Host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.HousingId));
    }

    [TestCase(true, false, "valid")]
    [TestCase(false, true, "valid")]
    [TestCase(false, false, "unknown")]
    public void Insertion_CancelUnownedOrUnsupported_PreservesSourceAndEmptyHousing(
        bool cancel, bool unowned, string identity)
    {
        var source = new PhysicalTestSlot();
        var original = new PhysicalTestItem();
        if (identity == "unknown")
        {
            original.Id = "unknown";
        }

        source.TransferHere(original);
        var cell = new NativeCellBoundary();

        Assert.That(cell.Insert(original, cancel, !unowned), Is.False);
        Assert.That(source.Item, Is.SameAs(original));
        Assert.That(original.Parent, Is.SameAs(source));
        Assert.That(cell.Component.Item, Is.Null);
        Assert.That(cell.Host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.HousingId));
        Assert.That(original.Fixed, Is.False);
    }

    [Test]
    public void Assembly_SecondComponentAndRepeatedDrop_PreserveBothOriginals()
    {
        var cell = NativeCellBoundary.Assembled();
        var first = cell.Component.Item;
        var source = new PhysicalTestSlot();
        var second = new PhysicalTestItem();
        source.TransferHere(second);

        Assert.That(cell.Insert(second), Is.False);
        Assert.That(cell.Insert(first!), Is.False);
        Assert.That(cell.Component.Item, Is.SameAs(first));
        Assert.That(source.Item, Is.SameAs(second));
        Assert.That(first!.Count + second.Count, Is.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Extraction_CancelOrFullDestination_PreservesUnlockedComponent(bool cancel)
    {
        var cell = NativeCellBoundary.Assembled();
        var original = cell.Component.Item!;
        original.Fixed = false;
        var destination = new PhysicalTestSlot();
        PhysicalTestItem? blocker = null;
        if (!cancel)
        {
            blocker = new PhysicalTestItem { Id = "blocker" };
            destination.TransferHere(blocker);
        }

        Assert.That(cell.Extract(destination, cancel), Is.False);
        Assert.That(cell.Component.Item, Is.SameAs(original));
        Assert.That(original.Parent, Is.SameAs(cell.Component));
        Assert.That(destination.Item, Is.SameAs(blocker));
        Assert.That(cell.Host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.CellId));
    }

    [Test]
    public void Extraction_RepeatedCompletionAndSecondRequest_DoNotDuplicateOrChangeAnotherHost()
    {
        var cell = NativeCellBoundary.Assembled();
        var other = NativeCellBoundary.Assembled();
        var original = cell.Component.Item!;
        var otherOriginal = other.Component.Item;
        original.Fixed = false;
        var destination = new PhysicalTestSlot();

        Assert.That(cell.Extract(destination), Is.True);
        cell.Callbacks.Removed();
        Assert.That(cell.Extract(new PhysicalTestSlot()), Is.False);
        Assert.That(destination.Item, Is.SameAs(original));
        Assert.That(other.Component.Item, Is.SameAs(otherOriginal));
        Assert.That(otherOriginal!.Fixed, Is.True);
        Assert.That(other.Host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.CellId));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DecoderInsertion_PreservesSavedFixedStateAndReference(bool fixedInPlace)
    {
        var cell = new NativeCellBoundary { Restoring = true };
        cell.Host.Id = StorageCellDisassemblyRecipe.CellId;
        var original = new PhysicalTestItem { Fixed = fixedInPlace };
        cell.Component.TransferHere(original);

        Assert.That(cell.Callbacks.Inserted(), Is.True);
        cell.Restoring = false;
        Assert.That(cell.Callbacks.IsConsistent(), Is.True);
        Assert.That(cell.Component.Item, Is.SameAs(original));
        Assert.That(original.Fixed, Is.EqualTo(fixedInPlace));
    }

    [Test]
    public void DecoderUnknownChild_RemovalRejectedAndForcedCompletionRetainsHostIdentity()
    {
        var cell = new NativeCellBoundary { Restoring = true };
        cell.Host.Id = StorageCellDisassemblyRecipe.CellId;
        var unknown = new PhysicalTestItem { Id = "missing.mod.component" };
        cell.Component.TransferHere(unknown);
        cell.Callbacks.Inserted();
        cell.Restoring = false;

        Assert.That(cell.Callbacks.CanRemove(), Is.False);
        Assert.That(cell.Extract(new PhysicalTestSlot()), Is.False);
        Assert.That(cell.Component.Item, Is.SameAs(unknown));
        cell.Callbacks.Removing(true);
        var forcedDestination = new PhysicalTestSlot();
        forcedDestination.TransferHere(unknown);
        cell.Callbacks.Removed();
        Assert.That(cell.Host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.CellId));
        Assert.That(cell.Callbacks.IsConsistent(), Is.False);
    }

    [Test]
    public void NonEmptyStorageCell_RefusesComponentExtractionWithoutChangingOwnership()
    {
        var cell = NativeCellBoundary.Assembled();
        var original = cell.Component.Item!;
        original.Fixed = false;
        cell.StorageState = StorageContentsState.NonEmpty;
        var destination = new PhysicalTestSlot();

        Assert.That(cell.Callbacks.GetRemovalFailure(), Is.EqualTo(
            ProbablyStolen.DigitalStorage.Common.Application.Transfers.CellRemovalFailure.STORAGE_CELL_NOT_EMPTY));
        Assert.That(cell.Callbacks.CanRemove(), Is.False);
        Assert.That(cell.Extract(destination), Is.False);
        Assert.That(cell.Component.Item, Is.SameAs(original));
        Assert.That(original.Parent, Is.SameAs(cell.Component));
        Assert.That(destination.Item, Is.Null);
    }

    [Test]
    public void ViewFailureAfterCommittedInsertion_RetainsOriginalGraphAndDoesNotAllocateReplacement()
    {
        var cell = new NativeCellBoundary { ThrowPresentation = true };
        var original = new PhysicalTestItem();
        var source = new PhysicalTestSlot();
        source.TransferHere(original);

        Assert.Throws<InvalidOperationException>(new Action(() => cell.Insert(original)));
        Assert.That(cell.Component.Item, Is.SameAs(original));
        Assert.That(source.Item, Is.Null);
        Assert.That(original.Parent, Is.SameAs(cell.Component));
        Assert.That(original.Count, Is.EqualTo(1));
        Assert.That(cell.Host.Id, Is.EqualTo(StorageCellDisassemblyRecipe.HousingId));
        Assert.That(cell.Callbacks.CanRemove(), Is.False);
        Assert.That(cell.Callbacks.IsConsistent(), Is.False);
    }
}
