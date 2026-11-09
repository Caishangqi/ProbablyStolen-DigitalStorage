using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network;

/// <summary>An immutable machine snapshot used only while computing one current topology graph.</summary>
public sealed class GridNode
{
    /// <summary>Creates a node with a scan-local ID and already validated configuration and slot facts.</summary>
    public GridNode(int id, GridNodeKind kind, int powerUsage, int occupiedDriveSlots = 0,
        int driveSlotCapacity = 0, IReadOnlyList<IStorageCell>? storageCells = null)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (!Enum.IsDefined(typeof(GridNodeKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (powerUsage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(powerUsage));
        }

        if (kind == GridNodeKind.Drive)
        {
            if (driveSlotCapacity != 14 || occupiedDriveSlots < 0 || occupiedDriveSlots > driveSlotCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(occupiedDriveSlots));
            }
        }
        else if (occupiedDriveSlots != 0 || driveSlotCapacity != 0 || storageCells is { Count: > 0 })
        {
            throw new ArgumentException("Only a drive node can contain drive slot facts.");
        }

        if (storageCells != null)
        {
            for (var index = 0; index < storageCells.Count; index++)
            {
                if (storageCells[index] == null)
                {
                    throw new ArgumentException("Storage cell snapshots cannot be null.", nameof(storageCells));
                }
            }
        }

        Id = id;
        Kind = kind;
        PowerUsage = powerUsage;
        OccupiedDriveSlots = occupiedDriveSlots;
        DriveSlotCapacity = driveSlotCapacity;
        StorageCells = storageCells ?? Array.Empty<IStorageCell>();
    }

    /// <summary>Gets an ephemeral ID unique within the current graph snapshot.</summary>
    public int Id { get; }

    /// <summary>Gets the machine role.</summary>
    public GridNodeKind Kind { get; }

    /// <summary>Gets configured game-native power usage for one turn.</summary>
    public int PowerUsage { get; }

    /// <summary>Gets the number of occupied storage bays for a drive.</summary>
    public int OccupiedDriveSlots { get; }

    /// <summary>Gets the number of storage bays for a drive.</summary>
    public int DriveSlotCapacity { get; }

    /// <summary>Gets storage cells currently attached to this drive snapshot.</summary>
    public IReadOnlyList<IStorageCell> StorageCells { get; }
}
