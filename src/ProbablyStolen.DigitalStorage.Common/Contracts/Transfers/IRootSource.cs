using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>Offers item roots in a fixed order (page, then row, then column) without changing anything.</summary>
public interface IRootSource
{
    /// <summary>Gets the first remaining root, read from the live pages each time.</summary>
    bool TryPeekFirst(out RootSnapshot root);

    /// <summary>Gets every remaining root in the same order as <see cref="TryPeekFirst"/>.</summary>
    IEnumerable<RootSnapshot> Snapshot();
}
