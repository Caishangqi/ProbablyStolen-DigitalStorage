using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>The only place that computes a cell's effective type limit.</summary>
public static class CellTypeLimit
{
    /// <summary>The highest effective type limit.</summary>
    public const int Max = 192;

    /// <summary>Gets the tier base plus every extension, clamped to <c>[1, Max]</c>.</summary>
    /// <exception cref="ArgumentNullException">No tier or extension collection was supplied.</exception>
    /// <exception cref="ArgumentException">An extension has no source, repeats a source, or has a negative bonus.</exception>
    public static int Effective(StorageCellTier tier, IEnumerable<TypeLimitExtension> extensions)
    {
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        if (extensions == null) throw new ArgumentNullException(nameof(extensions));

        long total = tier.GetTotalTypes();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            if (extension.SourceId == null)
                throw new ArgumentException("A type limit extension needs a source.", nameof(extensions));
            if (!sources.Add(extension.SourceId))
                throw new ArgumentException($"The type limit source '{extension.SourceId}' is repeated.", nameof(extensions));
            if (extension.Bonus < 0)
                throw new ArgumentException($"The type limit source '{extension.SourceId}' has a negative bonus.", nameof(extensions));
            total += extension.Bonus;
        }

        return (int)Math.Max(1, Math.Min(total, Max));
    }
}
