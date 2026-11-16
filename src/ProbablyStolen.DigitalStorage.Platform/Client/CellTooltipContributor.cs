using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class CellTooltipContributor : INativeTooltipContributor
{
    private static readonly Identifier ContributorId = new("digital_storage", "cell");
    private readonly BasicCellHandler _handler;

    internal CellTooltipContributor(BasicCellHandler handler) => _handler = handler;

    public Identifier Identity => ContributorId;

    public void Append(NativeTooltipContext context)
    {
        if (StorageCellTier.FromCellId(context.Item.identifier) != null)
            _handler.AddCellInformationToTooltip(context.Builder, context.Item, () => context.IsActive);
    }
}
