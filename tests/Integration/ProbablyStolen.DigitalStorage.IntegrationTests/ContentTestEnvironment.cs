using System;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

internal sealed class PhysicalTestItem
{
    internal string Id = StorageComponent.ItemId;
    internal int Count = 1;
    internal int Width = 1;
    internal int Height = 2;
    internal bool Fixed;
    internal readonly string Metadata = "original-component-metadata";
    internal PhysicalTestSlot? Parent;
    internal NativeCellBoundary? Cell;

    internal PhysicalItemSnapshot Snapshot() => new(Id, Count, Width, Height, Fixed);
}

internal sealed class PhysicalTestSlot
{
    internal PhysicalTestItem? Item;
    internal bool IsDrive;

    internal bool TransferHere(PhysicalTestItem item)
    {
        if (Item != null)
        {
            return false;
        }

        if (item.Parent != null)
        {
            if (!ReferenceEquals(item.Parent.Item, item))
            {
                throw new InvalidOperationException("Physical ownership is inconsistent.");
            }

            item.Parent.Item = null;
        }

        Item = item;
        item.Parent = this;
        return true;
    }
}

// These boundary doubles hold physical references. Rules and callback orchestration come from the product assembly.
internal sealed class NativeCellBoundary : ICellAssemblyPort
{
    internal readonly PhysicalTestItem Host;
    internal readonly PhysicalTestSlot Component = new();
    internal readonly CellAssemblyCallbacks Callbacks;
    internal bool Restoring;
    internal StorageContentsState StorageState = StorageContentsState.Empty;
    internal bool ThrowPresentation;

    internal NativeCellBoundary()
    {
        Host = new PhysicalTestItem { Id = StorageCellDisassemblyRecipe.HousingId, Cell = this };
        Callbacks = new CellAssemblyCallbacks(new StorageCellDisassemblyRecipe(new StorageComponent()), this);
    }

    public CellAssemblySnapshot Read() => new(Host.Snapshot(), Component.Item?.Snapshot(),
        Host.Parent?.IsDrive == true, Restoring, StorageState);

    public void FixComponent()
    {
        if (Component.Item == null)
        {
            throw new InvalidOperationException("No attached component.");
        }

        Component.Item.Fixed = true;
    }

    public void Present(bool assembled)
    {
        if (ThrowPresentation)
        {
            throw new InvalidOperationException("Injected native view failure.");
        }

        Host.Id = assembled ? StorageCellTier.FromComponentId(Component.Item?.Id)!.CellId : StorageCellDisassemblyRecipe.HousingId;
    }

    internal bool Insert(PhysicalTestItem candidate, bool cancel = false, bool ownershipAllowed = true)
    {
        if (!Callbacks.CanInsert(candidate.Snapshot(), ReferenceEquals(Component.Item, candidate), ownershipAllowed) ||
            cancel || !Component.TransferHere(candidate))
        {
            return false;
        }

        return Callbacks.Inserted();
    }

    internal bool Extract(PhysicalTestSlot destination, bool cancel = false)
    {
        var child = Component.Item;
        if (child == null || !Callbacks.CanRemove() || destination.Item != null || cancel)
        {
            return false;
        }

        Callbacks.Removing(true);
        if (!destination.TransferHere(child))
        {
            return false;
        }

        Callbacks.Removed();
        return true;
    }

    internal static NativeCellBoundary Assembled()
    {
        var result = new NativeCellBoundary();
        var component = new PhysicalTestItem();
        if (!result.Insert(component))
        {
            throw new InvalidOperationException("Fixture assembly was rejected.");
        }

        return result;
    }
}

internal sealed class NativeDriveBoundary : IMachineContentsPort
{
    internal readonly PhysicalTestSlot[] Bays = new PhysicalTestSlot[14];
    internal readonly PhysicalTestSlot Modules = new();
    internal readonly PhysicalTestSlot Battery = new();
    internal readonly object Window = new();
    internal readonly DriveBayBinding Binding;
    internal readonly MachineContentLifecycle Lifecycle;
    internal int RestoreCalls;
    internal bool Decoding;
    internal bool IsOpen;
    internal bool FailRestore;

    internal NativeDriveBoundary()
    {
        var ports = new ICellBayPort[Bays.Length];
        for (var index = 0; index < Bays.Length; index++)
        {
            Bays[index] = new PhysicalTestSlot { IsDrive = true };
            ports[index] = new BayPort(Bays[index]);
        }

        Binding = new DriveBayBinding(
            new DriveCellInventory(new StorageCellDisassemblyRecipe(new StorageComponent())), ports);
        Lifecycle = new MachineContentLifecycle(this);
    }

    internal bool Insert(int bay, NativeCellBoundary cell, bool cancel = false)
    {
        if (!Binding.CanInsert(bay, cell.Read(),
                bay >= 0 && bay < Bays.Length && ReferenceEquals(Bays[bay].Item, cell.Host), true) || cancel)
        {
            return false;
        }

        return Bays[bay].TransferHere(cell.Host);
    }

    internal void Open()
    {
        IsOpen = true;
        Lifecycle.RestoreIfReady(Decoding);
    }

    internal void Close()
    {
        IsOpen = false;
    }

    public void RestoreModules()
    {
        RestoreCalls++;
        if (FailRestore)
        {
            throw new InvalidOperationException("Injected native restoration failure.");
        }
    }

    private sealed class BayPort : ICellBayPort
    {
        private readonly PhysicalTestSlot _slot;

        internal BayPort(PhysicalTestSlot slot)
        {
            _slot = slot;
        }

        public CellAssemblySnapshot? Read() => _slot.Item?.Cell?.Read();
    }
}
