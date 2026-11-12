using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>The game's random source for module rolls.</summary>
internal sealed class NativeRandom : IRandom
{
    internal static readonly NativeRandom Instance = new();

    private NativeRandom()
    {
    }

    public int Next(int minInclusive, int maxExclusive) => RNG.GetRandomIntExculsive(minInclusive, maxExclusive);
}
