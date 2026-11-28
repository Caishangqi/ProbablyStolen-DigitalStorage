using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Platform.Identity;

/// <summary>
/// Feature identifier prefixes left out of item types. Published once when the content registries freeze, before any
/// game loads, so every type read in a session uses the same rules.
/// </summary>
internal sealed class VolatileFeatureRules
{
    internal static readonly VolatileFeatureRules None = new(Array.Empty<string>());
    private static volatile VolatileFeatureRules _current = None;
    private readonly string[] _prefixes;

    internal VolatileFeatureRules(IEnumerable<string> prefixes)
    {
        if (prefixes == null) throw new ArgumentNullException(nameof(prefixes));
        _prefixes = prefixes.Distinct(StringComparer.Ordinal).OrderBy(prefix => prefix, StringComparer.Ordinal).ToArray();
        if (_prefixes.Any(string.IsNullOrEmpty))
            throw new ArgumentException("A volatile feature prefix cannot be empty.", nameof(prefixes));
    }

    /// <summary>Gets the rules of the current session.</summary>
    internal static VolatileFeatureRules Current => _current;

    internal IReadOnlyList<string> Prefixes => _prefixes;

    internal static void Publish(VolatileFeatureRules rules) => _current = rules ?? throw new ArgumentNullException(nameof(rules));

    internal static void Reset() => _current = None;

    internal bool IsVolatile(string? identifier)
    {
        if (identifier == null) return false;
        foreach (var prefix in _prefixes)
            if (identifier.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }
}
