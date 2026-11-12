using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Api.Storage;

/// <summary>
/// Items are native roots that carry internal state, so they cannot be recreated from a key: insertion takes the
/// caller's root and extraction moves stored roots into the caller's inventory.
/// </summary>
/// <remarks>
/// Main thread only. Every call goes through the same atomic root transfer as the terminal: a unit either stays where
/// it was or ends in the destination. An offline network, an unavailable cell or a save that is still decoding reports
/// <see cref="StorageStatus.Unavailable"/>, never an empty inventory or a success. <see cref="Actionable.Simulate"/>
/// changes nothing.
/// </remarks>
public interface IStorage
{
    /// <summary>Gets whether this storage should receive the type before storage that does not prefer it.</summary>
    bool IsPreferredStorageFor(ItemKey what, IActionSource source);

    /// <summary>
    /// Stores a whole root that is not yet in this network. Only <paramref name="units"/> equal to the root's unit count
    /// is accepted; a part of a stack reports <see cref="StorageStatus.Rejected"/>.
    /// </summary>
    StorageResult Insert(GameItem root, int units, Actionable mode, IActionSource source);

    /// <summary>
    /// Moves stored roots of a type into <paramref name="destination"/>, lowest drive priority first, until at least
    /// <paramref name="amount"/> units moved. Whole roots move, so more than requested can arrive.
    /// </summary>
    StorageResult Extract(ItemKey what, long amount, GameGridInventory destination, Actionable mode, IActionSource source);

    /// <summary>
    /// Moves one chosen stored root into <paramref name="destination"/>; no other root of the same type moves.
    /// <paramref name="units"/> must equal the root's unit count: the game's stack split does not keep item state, so the
    /// API never splits a stack (a part reports <see cref="StorageStatus.Rejected"/>). If the destination has room for
    /// only part of the root, the result is <see cref="StorageStatus.Partial"/>. A root that is not stored in this network
    /// reports <see cref="StorageStatus.NotStored"/>.
    /// </summary>
    StorageResult ExtractRoot(GameItem root, int units, GameGridInventory destination, Actionable mode, IActionSource source);

    /// <summary>
    /// Adds the stored units of every type to the counter. Returns <see cref="StorageStatus.Done"/>, or the reason the
    /// network could not be read (<see cref="StorageStatus.Unavailable"/>, <see cref="StorageStatus.WrongThread"/>), in
    /// which case nothing is added: an unreadable network is not an empty one.
    /// </summary>
    StorageStatus GetAvailableStacks(KeyCounter output);

    /// <summary>Gets a short description for logs.</summary>
    string GetDescription();
}
