using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Plans where one whole item root goes in one cell. Native stacks are never split. Strip pages form one chain per
/// slot size; anchor and legacy pages never receive items. The partition is checked by the caller.
/// </summary>
public static class CellStorePlanner
{
    /// <summary>Plans one root; the first failed check is the rejection.</summary>
    public static StorePlan Plan(CellView cell, StoreCandidate item, PlanScope scope)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (item.Key == null) throw new ArgumentException("A candidate needs a type.", nameof(item));

        var insert = scope == PlanScope.Insert;
        if (insert && cell.WriteState == CellWriteState.ExtractOnly) return StorePlan.Reject(StoreRejection.ReadOnly);
        if (!StripLayout.Fits(item.Size)) return StorePlan.Reject(StoreRejection.TooLarge);

        if (insert)
        {
            var isNewType = !cell.Contains(item.Key);
            if (isNewType && cell.TypeCount >= cell.TypeLimit) return StorePlan.Reject(StoreRejection.TypeLimit);

            var area = CellUsage.AreaOf(item.Size, item.Units);
            var types = cell.TypeCount + (isNewType ? 1 : 0);
            var usedAfter = CellView.UsedBytes(types, checked(cell.Usage.AreaUnits + area), cell.Rules.Tier.GetTypeBudget(),
                cell.TypeLimit);
            if (usedAfter > cell.Rules.Tier.GetBytes()) return StorePlan.Reject(StoreRejection.Capacity);

            if (cell.EqualDistribution &&
                checked(cell.Usage.Get(item.Key).AreaUnits + area) > cell.EqualShareQuota())
                return StorePlan.Reject(StoreRejection.DistributionQuota);
        }

        foreach (var page in cell.ChainOf(item.Size))
        {
            var slot = cell.Pages[page].FirstFreeSlot();
            if (slot >= 0) return StorePlan.At(page, slot);
        }

        var pageLimit = insert ? cell.Rules.MaxPages : LayoutDescriptor.MaxPages;
        return cell.Pages.Count < pageLimit
            ? StorePlan.OnNewPage(cell.Pages.Count, StripLayout.PageFor(item.Size))
            : StorePlan.Reject(StoreRejection.PhysicalLimit);
    }
}
