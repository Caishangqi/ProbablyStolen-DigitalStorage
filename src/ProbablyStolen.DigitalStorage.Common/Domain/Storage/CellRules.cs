using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Tier-dependent page rules of a cell; page geometry itself comes from <see cref="StripLayout"/>.</summary>
/// <remarks>
/// Capacity is limited by bytes and types only; there is no separate page limit per tier. Appending stops only at
/// the restorable layout limit <see cref="LayoutDescriptor.MaxPages"/>, which keeps every saved cell rebuildable on load.
/// </remarks>
public sealed class CellRules
{
    /// <summary>Creates rules with an explicit page limit; tests use it to reach the limit quickly.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The limit is outside 1 to <see cref="LayoutDescriptor.MaxPages"/>.</exception>
    public CellRules(StorageCellTier tier, int maxPages)
    {
        Tier = tier ?? throw new ArgumentNullException(nameof(tier));
        if (maxPages < 1 || maxPages > LayoutDescriptor.MaxPages)
            throw new ArgumentOutOfRangeException(nameof(maxPages), maxPages, "The page limit is outside the descriptor limits.");
        MaxPages = maxPages;
    }

    /// <summary>Gets the rules of a tier: pages may be appended up to the restorable layout limit.</summary>
    public static CellRules For(StorageCellTier tier) =>
        new(tier ?? throw new ArgumentNullException(nameof(tier)), LayoutDescriptor.MaxPages);

    /// <summary>Gets the tier.</summary>
    public StorageCellTier Tier { get; }

    /// <summary>Gets the page count up to which new strip pages may be appended.</summary>
    public int MaxPages { get; }
}
