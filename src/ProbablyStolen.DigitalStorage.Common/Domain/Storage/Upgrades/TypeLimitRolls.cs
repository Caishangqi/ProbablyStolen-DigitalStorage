using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>The values of one type limit module, rolled once when the item is created.</summary>
public sealed class TypeLimitRoll
{
    /// <summary>Creates a roll; <see cref="TypeLimitRolls.IsValid"/> checks it against its quality's ranges.</summary>
    public TypeLimitRoll(ModuleQuality quality, int add, int percent)
    {
        Quality = quality;
        Add = add;
        Percent = percent;
    }

    /// <summary>Gets the quality grade.</summary>
    public ModuleQuality Quality { get; }

    /// <summary>Gets the flat type limit bonus.</summary>
    public int Add { get; }

    /// <summary>Gets the percentage type limit bonus, in whole percent.</summary>
    public int Percent { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Quality} +{Add} +{Percent}%";
}

/// <summary>Quality ranges, rolling and pricing of type limit modules.</summary>
public static class TypeLimitRolls
{
    /// <summary>The default vendor weight of common modules.</summary>
    public const int CommonWeight = 60;

    /// <summary>The default vendor weight of rare modules.</summary>
    public const int RareWeight = 30;

    /// <summary>The default vendor weight of epic modules.</summary>
    public const int EpicWeight = 10;

    /// <summary>Gets the inclusive range of the flat bonus.</summary>
    public static (int Min, int Max) AddRange(ModuleQuality quality) => quality switch
    {
        ModuleQuality.Common => (8, 16),
        ModuleQuality.Rare => (16, 32),
        ModuleQuality.Epic => (32, 64),
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown module quality.")
    };

    /// <summary>Gets the inclusive range of the percentage bonus.</summary>
    public static (int Min, int Max) PercentRange(ModuleQuality quality) => quality switch
    {
        ModuleQuality.Common => (5, 10),
        ModuleQuality.Rare => (10, 20),
        ModuleQuality.Epic => (20, 30),
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown module quality.")
    };

    /// <summary>Gets the base price a roll's price scales from.</summary>
    public static long BasePrice(ModuleQuality quality) => quality switch
    {
        ModuleQuality.Common => 100,
        ModuleQuality.Rare => 200,
        ModuleQuality.Epic => 400,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown module quality.")
    };

    /// <summary>Draws a quality by weight.</summary>
    public static ModuleQuality RollQuality(IRandom random, int common = CommonWeight, int rare = RareWeight,
        int epic = EpicWeight)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (common < 0 || rare < 0 || epic < 0)
            throw new ArgumentOutOfRangeException(nameof(common), "Weights are not negative.");
        var total = checked(common + rare + epic);
        if (total == 0) throw new ArgumentOutOfRangeException(nameof(common), "At least one weight is positive.");
        var draw = random.Next(0, total);
        if (draw < common) return ModuleQuality.Common;
        return draw < common + rare ? ModuleQuality.Rare : ModuleQuality.Epic;
    }

    /// <summary>Rolls both values independently and uniformly within the quality's inclusive ranges.</summary>
    public static TypeLimitRoll Roll(ModuleQuality quality, IRandom random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        var (addMin, addMax) = AddRange(quality);
        var (percentMin, percentMax) = PercentRange(quality);
        return new TypeLimitRoll(quality, random.Next(addMin, addMax + 1), random.Next(percentMin, percentMax + 1));
    }

    /// <summary>Gets whether a roll's quality is known and both values lie within its ranges.</summary>
    public static bool IsValid(TypeLimitRoll? roll)
    {
        if (roll == null || !Enum.IsDefined(typeof(ModuleQuality), roll.Quality)) return false;
        var (addMin, addMax) = AddRange(roll.Quality);
        var (percentMin, percentMax) = PercentRange(roll.Quality);
        return roll.Add >= addMin && roll.Add <= addMax && roll.Percent >= percentMin && roll.Percent <= percentMax;
    }

    /// <summary>
    /// Gets <c>base * (0.8 + 0.4 * p)</c>, rounded half away from zero, where p averages each value's position in
    /// its range (0 at the minimum, 1 at the maximum).
    /// </summary>
    /// <exception cref="ArgumentException">The roll is invalid.</exception>
    public static long Price(TypeLimitRoll roll)
    {
        if (!IsValid(roll)) throw new ArgumentException("An invalid roll has no price.", nameof(roll));
        var position = (Position(roll.Add, AddRange(roll.Quality)) + Position(roll.Percent, PercentRange(roll.Quality))) / 2m;
        return (long)Math.Round(BasePrice(roll.Quality) * (0.8m + 0.4m * position), MidpointRounding.AwayFromZero);
    }

    private static decimal Position(int value, (int Min, int Max) range) =>
        range.Max == range.Min ? 0.5m : (decimal)(value - range.Min) / (range.Max - range.Min);
}
