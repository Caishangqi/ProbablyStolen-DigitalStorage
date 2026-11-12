using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>
/// Receives item roots and decides where each goes. <see cref="Actionable.Simulate"/> only plans;
/// <see cref="Actionable.Modulate"/> appends a page if needed, moves the native root with <c>ItemMove</c>, verifies
/// it left the source, and updates the target view.
/// </summary>
public interface IRootSink
{
    /// <summary>Accepts one root.</summary>
    TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source);
}
