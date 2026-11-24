using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Commerce;

internal static class DigitalStorageBuybackTerms
{
    internal const int VisitBudget = 800;

    internal static bool Accepts(string? itemId) => StorageCellTier.FromComponentId(itemId) != null;

    internal static bool CanAccept(string? itemId, long negotiatedValue, int remainingBudget, int nativeBudget) =>
        Accepts(itemId) && negotiatedValue > 0 && remainingBudget > 0 && negotiatedValue <= remainingBudget && nativeBudget > 0;

    internal static int RemainingAfterSale(int remainingBudget, int payout)
    {
        if (remainingBudget <= 0) return 0;
        if (payout <= 0) return remainingBudget;
        return payout >= remainingBudget ? 0 : remainingBudget - payout;
    }
}
