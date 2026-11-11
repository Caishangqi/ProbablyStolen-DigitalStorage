using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>A supported storage cell tier and its exact native identities.</summary>
public sealed class StorageCellTier
{
    private static readonly ReadOnlyCollection<StorageCellTier> Tiers = Array.AsReadOnly(new[]
    {
        new StorageCellTier("1k", 1, 250, 50),
        new StorageCellTier("4k", 4, 750, 25),
        new StorageCellTier("16k", 16, 2250, 15),
        new StorageCellTier("64k", 64, 6750, 8),
        new StorageCellTier("256k", 256, 20250, 2)
    });

    private StorageCellTier(string name, int multiplier, int componentValue, int merchantWeight)
    {
        Name = name;
        Multiplier = multiplier;
        ComponentValue = componentValue;
        MerchantWeight = merchantWeight;
        ComponentId = "digital_storage.cell_component_" + name;
        CellId = "digital_storage.item_storage_cell_" + name;
    }

    /// <summary>The base type limit of every tier.</summary>
    public const int BaseTypeLimit = 128;

    /// <summary>Gets the immutable ordered catalog.</summary>
    public static IReadOnlyList<StorageCellTier> All => Tiers;
    /// <summary>Selects a merchant tier from an inclusive 1..100 draw.</summary>
    public static StorageCellTier FromMerchantRoll(int roll)
    {
        if (roll < 1 || roll > 100) throw new ArgumentOutOfRangeException(nameof(roll));
        foreach (var tier in Tiers)
        {
            roll -= tier.MerchantWeight;
            if (roll <= 0) return tier;
        }
        throw new InvalidOperationException("Merchant tier weights must total 100.");
    }
    /// <summary>Gets the tier suffix.</summary>
    public string Name { get; }
    /// <summary>Gets the multiplier applied to 1k capacity.</summary>
    public int Multiplier { get; }
    /// <summary>Gets the component base value.</summary>
    public int ComponentValue { get; }
    /// <summary>Gets the merchant selection weight.</summary>
    public int MerchantWeight { get; }
    /// <summary>Gets the exact component identity.</summary>
    public string ComponentId { get; }
    /// <summary>Gets the exact assembled cell identity.</summary>
    public string CellId { get; }

    /// <summary>Finds a tier by exact component identity, or null.</summary>
    public static StorageCellTier? FromComponentId(string? id)
    {
        foreach (var tier in Tiers)
            if (string.Equals(tier.ComponentId, id, StringComparison.Ordinal)) return tier;
        return null;
    }

    /// <summary>Finds a tier by exact cell identity, or null.</summary>
    public static StorageCellTier? FromCellId(string? id)
    {
        foreach (var tier in Tiers)
            if (string.Equals(tier.CellId, id, StringComparison.Ordinal)) return tier;
        return null;
    }

    /// <summary>Gets the byte capacity: 1024 per multiplier step.</summary>
    public long GetBytes() => 1024L * Multiplier;

    /// <summary>Gets the bytes each stored type costs: 4 per multiplier step.</summary>
    public long GetBytesPerType() => 4L * Multiplier;

    /// <summary>Gets the base type limit before extensions.</summary>
    public int GetTotalTypes() => BaseTypeLimit;
}
