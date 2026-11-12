using ProbablyStolen.DigitalStorage.Api.Networking.Storage;

namespace ProbablyStolen.DigitalStorage.Api.Networking;

/// <summary>A view of one digital network.</summary>
/// <remarks>
/// A view describes the network when it was looked up. Networks change when the player moves machines, so look a grid up
/// again instead of keeping it across frames.
/// </remarks>
public interface IGrid
{
    /// <summary>Gets whether the network has its controller and enough power.</summary>
    bool IsOnline { get; }

    /// <summary>Gets the storage service of the network.</summary>
    IStorageService GetStorageService();
}

/// <summary>Identifies a machine and the network it belongs to.</summary>
public interface IGridNode
{
    /// <summary>Gets the network the machine belongs to.</summary>
    IGrid Grid { get; }

    /// <summary>Gets the native item of the machine (terminal, drive, controller).</summary>
    GameItem Owner { get; }

    /// <summary>Gets whether the network is online.</summary>
    bool IsOnline { get; }
}
