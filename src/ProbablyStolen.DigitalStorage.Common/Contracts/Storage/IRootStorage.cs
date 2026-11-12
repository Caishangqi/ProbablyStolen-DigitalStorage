using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

/// <summary>
/// Storage that moves whole native item roots. Items carry internal state and cannot be
/// recreated from a key, so insertion takes the caller's roots and extraction moves roots into the caller's target.
/// Main thread only; callers refuse while a save is decoding.
/// </summary>
/// <remarks>
/// Internal root-transfer contract (roots are addressed by page position). Public only because Common is a separate
/// assembly from Platform; it is not a supported API for other mods. The public storage contract is
/// <c>ProbablyStolen.DigitalStorage.Api.Storage.IStorage</c>.
/// </remarks>
public interface IRootStorage
{
    /// <summary>Gets whether this storage should receive a type before non-preferred storage.</summary>
    bool IsPreferredStorageFor(ItemKey what, IActionSource source);

    /// <summary>Takes roots from the source until it is empty or a root is refused.</summary>
    InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source);

    /// <summary>Moves whole roots of a type into the destination until at least <paramref name="amount"/> units moved.</summary>
    ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode, IActionSource source);

    /// <summary>Adds the stored units of every type to the counter.</summary>
    void GetAvailableStacks(KeyCounter output);

    /// <summary>Gets a short description for logs.</summary>
    string GetDescription();
}
