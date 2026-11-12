namespace ProbablyStolen.DigitalStorage.Common.Application.Events;

/// <summary>Why a storage network may now read differently.</summary>
public enum NetworkChangeReason
{
    /// <summary>A network machine entered an inventory.</summary>
    MachineAdded,

    /// <summary>A network machine is leaving an inventory.</summary>
    MachineRemoved,

    /// <summary>A storage cell entered a drive bay.</summary>
    CellInserted,

    /// <summary>A storage cell left a drive bay.</summary>
    CellRemoved,

    /// <summary>A controller battery was inserted or removed.</summary>
    BatteryChanged,

    /// <summary>A controller finished its turn-end power draw.</summary>
    PowerCycle,

    /// <summary>The mod configuration, such as machine power usage, was applied.</summary>
    ConfigChanged,

    /// <summary>
    /// Stored items moved through the public API (another mod or an integration plugin), outside any terminal.
    /// </summary>
    ContentsChanged
}

/// <summary>
/// Signals that cached network views are stale. It carries no item reference; subscribers reread the network
/// instead of acting on an item that may already be leaving its inventory.
/// </summary>
public readonly struct NetworkChanged
{
    /// <summary>Creates the signal for one reason.</summary>
    public NetworkChanged(NetworkChangeReason reason) => Reason = reason;

    /// <summary>Gets the change that raised the signal.</summary>
    public NetworkChangeReason Reason { get; }
}
