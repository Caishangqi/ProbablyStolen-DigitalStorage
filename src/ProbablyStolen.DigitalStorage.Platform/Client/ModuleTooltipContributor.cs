using System;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class ModuleTooltipContributor : INativeTooltipContributor
{
    private static readonly Identifier ContributorId = new("digital_storage", "module");
    private readonly ModText _text;
    private readonly Func<GameItem, CellModuleUsage?> _findUsage;

    internal ModuleTooltipContributor(ModText text, Func<GameItem, CellModuleUsage?> findUsage)
    {
        _text = text;
        _findUsage = findUsage;
    }

    public Identifier Identity => ContributorId;

    public void Append(NativeTooltipContext context)
    {
        if (StorageCellTier.FromCellId(context.Item.identifier) != null ||
            StorageCellTier.FromComponentId(context.Item.identifier) != null) return;
        ModuleChangeTooltipPresenter.AppendRemovalRefusal(context.Builder, context.Item, _text, _findUsage);
    }
}
