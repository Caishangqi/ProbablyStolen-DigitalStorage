namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>The quality grade of a type limit module; it sets the ranges its values roll in.</summary>
public enum ModuleQuality
{
    /// <summary>Common.</summary>
    Common = 0,

    /// <summary>Rare.</summary>
    Rare = 1,

    /// <summary>Epic.</summary>
    Epic = 2
}

/// <summary>A random source, injected so rolls can be tested with fixed sequences.</summary>
public interface IRandom
{
    /// <summary>Gets an integer in <c>[minInclusive, maxExclusive)</c>.</summary>
    int Next(int minInclusive, int maxExclusive);
}
