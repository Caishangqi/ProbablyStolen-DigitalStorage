using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Ports;

/// <summary>The native page operations of one cell; Platform <c>CellPages</c> implements it.</summary>
public interface ICellPagesPort
{
    /// <summary>
    /// Appends a strip page and writes the matching layout descriptor. On failure nothing changes and the result is
    /// false.
    /// </summary>
    bool TryAppendStripPage(PageGeometry page);

    /// <summary>
    /// Moves a root, resolved through its <see cref="RootSnapshot.Origin"/>, into a strip slot of this cell with
    /// <c>ItemMove</c>, and verifies it arrived. Returns <see cref="TransferOutcome.Returned"/> or
    /// <see cref="TransferOutcome.Unowned"/> when the native move failed.
    /// </summary>
    TransferStep MoveInto(RootSnapshot root, int page, int slot);

    /// <summary>Gets this cell's strip-page roots of one type, ordered by page, row and column.</summary>
    IRootSource StoredRoots(ItemKey key);
}
