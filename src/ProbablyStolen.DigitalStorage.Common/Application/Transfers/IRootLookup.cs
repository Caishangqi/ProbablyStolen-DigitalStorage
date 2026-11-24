using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>
/// A root source that can tell whether one snapshot is still among its roots without listing them all. The answer
/// must equal looking the snapshot up in <see cref="IRootSource.Snapshot"/>.
/// </summary>
internal interface IRootLookup
{
    bool Contains(RootSnapshot root);
}
