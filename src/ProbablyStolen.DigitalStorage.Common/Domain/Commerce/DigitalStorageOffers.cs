using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Commerce;

/// <summary>A kind of storage good a vendor can put on the counter.</summary>
public enum StorageGood
{
    /// <summary>An empty cell housing.</summary>
    Housing,

    /// <summary>A storage component of <see cref="StorageOffer.Tier"/>.</summary>
    Component,

    /// <summary>The equal distribution module.</summary>
    EqualDistributionModule,

    /// <summary>The stack display module.</summary>
    StackDisplayModule,

    /// <summary>A type limit module of <see cref="StorageOffer.Quality"/>.</summary>
    TypeLimitModule,

    /// <summary>An IO Port for item transfers between cells and a network.</summary>
    IoPort
}

/// <summary>One good offered for sale.</summary>
public sealed class StorageOffer
{
    private StorageOffer(StorageGood good, StorageCellTier? tier, ModuleQuality quality)
    {
        Good = good;
        Tier = tier;
        Quality = quality;
    }

    /// <summary>Gets the kind of good.</summary>
    public StorageGood Good { get; }

    /// <summary>Gets the component tier; set only for <see cref="StorageGood.Component"/>.</summary>
    public StorageCellTier? Tier { get; }

    /// <summary>Gets the module quality; meaningful only for <see cref="StorageGood.TypeLimitModule"/>.</summary>
    public ModuleQuality Quality { get; }

    /// <summary>Gets a housing offer.</summary>
    public static StorageOffer Housing { get; } = new(StorageGood.Housing, null, ModuleQuality.Common);

    /// <summary>Gets an equal distribution module offer.</summary>
    public static StorageOffer EqualDistribution { get; } = new(StorageGood.EqualDistributionModule, null, ModuleQuality.Common);

    /// <summary>Gets a stack display module offer.</summary>
    public static StorageOffer StackDisplay { get; } = new(StorageGood.StackDisplayModule, null, ModuleQuality.Common);

    /// <summary>Gets an IO Port offer.</summary>
    public static StorageOffer IoPort { get; } = new(StorageGood.IoPort, null, ModuleQuality.Common);

    /// <summary>Creates a component offer.</summary>
    public static StorageOffer Component(StorageCellTier tier) =>
        new(StorageGood.Component, tier ?? throw new ArgumentNullException(nameof(tier)), ModuleQuality.Common);

    /// <summary>Creates a type limit module offer.</summary>
    public static StorageOffer TypeLimit(ModuleQuality quality) => new(StorageGood.TypeLimitModule, null, quality);

    /// <inheritdoc />
    public override string ToString() => Good switch
    {
        StorageGood.Component => "Component " + Tier!.Name,
        StorageGood.TypeLimitModule => "TypeLimit " + Quality,
        StorageGood.IoPort => "IO Port",
        _ => Good.ToString()
    };
}

/// <summary>
/// The goods the data scientist and Dr. Jackson offer through the Wild network's digital storage upgrades, and the
/// day the data scientist visits. Rotations advance once per seven-day week of the store's day counter.
/// </summary>
public static class DigitalStorageOffers
{
    /// <summary>
    /// Gets whether the data scientist joins the customers on <paramref name="dayCounter"/>: the retired farmer's day,
    /// every seventh day with remainder 5, never on day 0.
    /// </summary>
    public static bool ScientistVisits(int dayCounter) => dayCounter > 0 && dayCounter % 7 == 5;

    /// <summary>Gets the data scientist's stock for a visit on <paramref name="dayCounter"/>.</summary>
    /// <param name="dayCounter">The store's day counter; selects the advanced rotation.</param>
    /// <param name="advanced">Whether the second digital storage upgrade is unlocked.</param>
    public static IReadOnlyList<StorageOffer> ScientistStock(int dayCounter, bool advanced)
    {
        var offers = new List<StorageOffer>
        {
            StorageOffer.Housing, StorageOffer.Housing,
            StorageOffer.Component(Tier("1k")), StorageOffer.Component(Tier("1k")),
            StorageOffer.Component(Tier("4k")),
            StorageOffer.TypeLimit(ModuleQuality.Common)
        };
        if (!advanced) return offers;
        switch (Week(dayCounter) % 3)
        {
            case 0:
                offers.Add(StorageOffer.Component(Tier("64k")));
                offers.Add(StorageOffer.TypeLimit(ModuleQuality.Rare));
                break;
            case 1:
                offers.Add(StorageOffer.Component(Tier("64k")));
                offers.Add(StorageOffer.EqualDistribution);
                offers.Add(StorageOffer.StackDisplay);
                break;
            default:
                offers.Add(StorageOffer.Component(Tier("16k")));
                offers.Add(StorageOffer.Component(Tier("16k")));
                offers.Add(StorageOffer.TypeLimit(ModuleQuality.Epic));
                break;
        }

        return offers;
    }

    /// <summary>Gets the additional component and IO Port offered after the third upgrade.</summary>
    public static IReadOnlyList<StorageOffer> ExpertStock() =>
        new[] { StorageOffer.Component(Tier("256k")), StorageOffer.IoPort };

    /// <summary>
    /// Gets the module and the component Dr. Jackson tries to stock on every visit once the second digital storage
    /// upgrade is unlocked, in addition to his random stock.
    /// </summary>
    public static IReadOnlyList<StorageOffer> JacksonGuaranteedStock(int dayCounter)
    {
        var week = Week(dayCounter);
        var module = (week % 3) switch
        {
            0 => StorageOffer.EqualDistribution,
            1 => StorageOffer.StackDisplay,
            _ => StorageOffer.TypeLimit(ModuleQuality.Common)
        };
        return new[] { module, StorageOffer.Component(Tier(week % 2 == 0 ? "16k" : "64k")) };
    }

    private static int Week(int dayCounter) => Math.Max(0, dayCounter) / 7;

    private static StorageCellTier Tier(string name)
    {
        foreach (var tier in StorageCellTier.All)
        {
            if (tier.Name == name) return tier;
        }

        throw new InvalidOperationException("Unknown storage cell tier: " + name);
    }
}
