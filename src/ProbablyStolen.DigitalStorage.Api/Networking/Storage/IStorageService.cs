using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Api.Storage;

namespace ProbablyStolen.DigitalStorage.Api.Networking.Storage;

/// <summary>Provides inventory operations and snapshots for a digital network.</summary>
public interface IStorageService
{
    /// <summary>Gets the network inventory that moves items.</summary>
    IStorage GetInventory();

    /// <summary>
    /// Gets the stored units per type now, as a copy the caller may change, or null when the network cannot be read
    /// (offline, a save is decoding, or the caller is off the main thread). Null never means empty.
    /// </summary>
    KeyCounter? GetCachedInventory();
}
