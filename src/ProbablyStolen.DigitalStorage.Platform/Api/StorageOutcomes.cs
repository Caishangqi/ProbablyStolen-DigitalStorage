using ProbablyStolen.DigitalStorage.Api.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Api;

/// <summary>
/// How the public storage facade turns guards and transfer results into <see cref="StorageResult"/>s. Kept free of
/// game types so the rules are tested without the game.
/// </summary>
internal static class StorageOutcomes
{
    /// <summary>Gets the refusal for a call that cannot run, or null when it may run. Thread first, then decoding, then power.</summary>
    internal static StorageResult? Refuse(bool isMainThread, bool decoding, bool online)
    {
        if (!isMainThread) return StorageResult.None(StorageStatus.WrongThread);
        if (decoding || !online) return StorageResult.None(StorageStatus.Unavailable);
        return null;
    }

    /// <summary>Whether a root request covers the whole root; the API never splits a stack.</summary>
    /// <remarks>
    /// The game's stack split (<c>GameItem.GetSplitItem</c>) creates a fresh item from the item directory and does not
    /// copy the item's state, so a split would lose tags such as the bought price or the stolen mark.
    /// </remarks>
    internal static bool IsWholeRoot(int units, int rootUnits) => units > 0 && units == rootUnits;

    /// <summary>The result of extracting a type: nothing moved is empty or refused; otherwise done or partial.</summary>
    internal static StorageResult Extracted(long moved, long requested, bool sourceWasEmpty)
    {
        if (moved <= 0) return StorageResult.None(sourceWasEmpty ? StorageStatus.Empty : StorageStatus.Rejected);
        return new StorageResult(moved, moved >= requested ? StorageStatus.Done : StorageStatus.Partial);
    }

    /// <summary>The result of extracting a whole root.</summary>
    internal static StorageResult ExtractedRoot(long moved, int requested, bool rootWasMissing)
    {
        if (rootWasMissing) return StorageResult.None(StorageStatus.NotStored);
        if (moved <= 0) return StorageResult.None(StorageStatus.Rejected);
        return new StorageResult(moved, moved >= requested ? StorageStatus.Done : StorageStatus.Partial);
    }

    /// <summary>
    /// Whether a call changed what the network stores, so open views must reread it: only a performed call that moved
    /// units. Simulations and refused calls change nothing.
    /// </summary>
    internal static bool ChangedContents(bool simulated, long movedUnits) => !simulated && movedUnits > 0;

    /// <summary>The result of storing a whole root: all of it or nothing.</summary>
    internal static StorageResult Inserted(long inserted, int units) =>
        inserted >= units ? new StorageResult(inserted, StorageStatus.Done) : StorageResult.None(StorageStatus.Rejected);
}
