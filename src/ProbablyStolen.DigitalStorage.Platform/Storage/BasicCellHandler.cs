using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Recognizes storage cells and describes them.</summary>
/// <remarks>
/// Cell recognition lives in this part so pure integration tests can link it without the tooltip's native text
/// dependencies; cell reading and lore are in <c>BasicCellHandler.Native.cs</c>.
/// </remarks>
internal sealed partial class BasicCellHandler
{
    /// <summary>Gets whether an item is an assembled storage cell with its storage component.</summary>
    internal static bool IsCell(GameItem? cell)
    {
        if (cell == null || StorageCellTier.FromCellId(cell.identifier) is not { } tier)
        {
            return false;
        }

        var component = CellContentWindow.Component(cell);
        return component != null && StorageCellTier.FromComponentId(component.identifier) == tier &&
            cell.unitCount == 1 && component.unitCount == 1;
    }
}
