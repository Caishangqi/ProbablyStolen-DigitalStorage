using System;

namespace ProbablyStolen.DigitalStorage.Compat.BrewingExpansion;

internal static class BrewingExpansionVersions
{
    private static readonly Version Minimum = new(3, 6, 8, 6);
    private static readonly Version MaximumExclusive = new(3, 7, 0, 0);

    internal static bool Supports(string version)
    {
        if (string.IsNullOrEmpty(version)) return false;
        foreach (var character in version)
            if (character != '.' && (character < '0' || character > '9')) return false;
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0) return false;
        var normalized = new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
        return normalized >= Minimum && normalized < MaximumExclusive;
    }
}
