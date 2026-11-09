namespace ProbablyStolen.DigitalStorage.Common.Contracts;

/// <summary>A single read of the host and its original component at a native callback boundary.</summary>
public sealed class CellAssemblySnapshot
{
    /// <summary>Captures the actual graph without creating replacement items.</summary>
    public CellAssemblySnapshot(PhysicalItemSnapshot host, PhysicalItemSnapshot? component,
        bool mounted = false, bool restoring = false,
        StorageContentsState storageState = StorageContentsState.Unavailable)
    {
        Host = host;
        Component = component;
        Mounted = mounted;
        Restoring = restoring;
        StorageState = storageState;
    }

    /// <summary>The physical housing or assembled cell.</summary>
    public PhysicalItemSnapshot Host { get; }

    /// <summary>The actual child, or null while the slot is empty.</summary>
    public PhysicalItemSnapshot? Component { get; }

    /// <summary>Whether the host currently belongs to a drive bay.</summary>
    public bool Mounted { get; }

    /// <summary>Whether native decoding is still connecting saved children.</summary>
    public bool Restoring { get; }

    /// <summary>Whether stored contents were positively read at this boundary.</summary>
    public StorageContentsState StorageState { get; }
}

/// <summary>Evidence state for the physical contents of a digital storage component.</summary>
public enum StorageContentsState
{
    /// <summary>Contents could not be positively established.</summary>
    Unavailable = 0,

    /// <summary>The component was positively read and contains no roots.</summary>
    Empty = 1,

    /// <summary>The component was positively read and contains roots.</summary>
    NonEmpty = 2,

    /// <summary>Contents or ownership require recovery before disassembly.</summary>
    RecoveryRequired = 3
}
