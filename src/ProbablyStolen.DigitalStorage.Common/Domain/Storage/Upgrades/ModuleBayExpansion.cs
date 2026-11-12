using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>
/// The module bay shape of a cell and how far the native module bay expansion kit may enlarge it per tier. The
/// shapes repeat the native standard machine bay: 7 columns, 1 marks a blocked cell and 0 an open one.
/// </summary>
public static class ModuleBayExpansion
{
    /// <summary>The bay width in grid cells.</summary>
    public const int Width = 7;

    /// <summary>The highest stage the native kit knows.</summary>
    public const int MaxNativeStage = 2;

    private static readonly string[] Shapes =
    {
        "10000010001000001110000010001000001",
        "10000010000000000000000000001000001",
        "00000000000000000000000000000000000"
    };

    /// <summary>Gets the furthest stage a tier allows: none for 1k and 4k, one for 16k and 64k, two for 256k.</summary>
    public static int MaxStage(StorageCellTier tier)
    {
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        return tier.Multiplier switch
        {
            <= 4 => 0,
            <= 64 => 1,
            _ => MaxNativeStage
        };
    }

    /// <summary>Gets whether the kit may expand a bay at this stage once more for the tier.</summary>
    public static bool CanExpand(int stage, StorageCellTier tier) => stage >= 0 && stage < MaxStage(tier);

    /// <summary>Gets whether a housing at this stage may take a component of the tier.</summary>
    public static bool Fits(int stage, StorageCellTier tier) => stage >= 0 && stage <= MaxStage(tier);

    /// <summary>Applies one expansion and restores the prior stage if a native operation fails.</summary>
    public static void Apply(int currentStage, StorageCellTier tier, Action<string, int> setShape,
        Action<int> setStage, Action validate)
    {
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        if (setShape == null) throw new ArgumentNullException(nameof(setShape));
        if (setStage == null) throw new ArgumentNullException(nameof(setStage));
        if (validate == null) throw new ArgumentNullException(nameof(validate));
        var nextStage = checked(currentStage + 1);
        if (!Fits(nextStage, tier)) throw new InvalidOperationException("The expanded stage exceeds this cell tier.");
        try
        {
            setShape(Shape(nextStage), Width);
            setStage(nextStage);
            validate();
        }
        catch (Exception failure)
        {
            try
            {
                setShape(Shape(currentStage), Width);
                setStage(currentStage);
                validate();
            }
            catch (Exception restoreFailure)
            {
                throw new AggregateException("Cell module bay expansion and restoration failed.", failure, restoreFailure);
            }

            throw;
        }
    }

    /// <summary>Gets the shape string of a stage.</summary>
    public static string Shape(int stage)
    {
        if (stage < 0 || stage > MaxNativeStage)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "The native kit has three stages.");
        return Shapes[stage];
    }
}
