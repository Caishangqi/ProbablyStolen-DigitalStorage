using System;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal static class ItemContentPriceRules
{
    internal static long WithExpansion(long baseValue, int stage, int maxStage, long pricePerStage)
    {
        if (maxStage < 0) throw new ArgumentOutOfRangeException(nameof(maxStage));
        if (pricePerStage < 0) throw new ArgumentOutOfRangeException(nameof(pricePerStage));
        if (stage < 0 || stage > maxStage) throw new InvalidOperationException("The module bay expansion stage is invalid for this item.");
        return checked(baseValue + (long)stage * pricePerStage);
    }
}
