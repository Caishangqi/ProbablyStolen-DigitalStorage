using System;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class StoredRootIdentityMismatch : RootTransferNotStartedException
{
    internal StoredRootIdentityMismatch(string reason, string expectedId, string? actualId, int cellUid, int page, int slot)
        : base($"A stored root on page {page} is no longer of its recorded type.")
    {
        Reason = reason;
        ExpectedId = Normalize(expectedId);
        ActualId = Normalize(actualId);
        CellUid = cellUid;
        Page = page;
        Slot = slot;
    }

    internal string Reason { get; }
    internal string ExpectedId { get; }
    internal string ActualId { get; }
    internal int CellUid { get; }
    internal int Page { get; }
    internal int Slot { get; }

    internal static string? Classify(ItemKey expected, ItemKey? actual, string? nativeId, bool readFailed)
    {
        if (expected.Equals(actual)) return null;
        if (!string.IsNullOrWhiteSpace(nativeId) && (expected.IsCanonical || expected.Identifier != "unknown") &&
            !string.Equals(expected.Identifier, nativeId, StringComparison.Ordinal)) return "item_id_changed";
        return !string.IsNullOrWhiteSpace(nativeId) && !readFailed && actual?.IsCanonical == true && expected.IsCanonical
            ? "identity_key_changed" : "identity_unreadable";
    }

    private static string Normalize(string? id) => string.IsNullOrWhiteSpace(id) ? "unknown" :
        id!.Substring(0, Math.Min(id.Length, LogFormatter.MaximumStringLength));
}
