using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Storage format version of a cell (<c>digital_storage.schema</c>). Each mod release migrates only the previous
/// schema; older cells are locked and the player upgrades through the listed releases, like GitLab's required
/// upgrade stops.
/// </summary>
public static class CellSchema
{
    /// <summary>The schema of a cell without a schema tag: 12×12 pages counted by <c>page_count</c>.</summary>
    public const int Untagged = 1;

    /// <summary>The schema written by this version: strip pages described by <c>digital_storage.layout</c>.</summary>
    public const int Current = 2;

    // The release that ships schema 2 and migrates schema 1 cells.
    private const string StripReleaseVersion = "0.4.0";

    /// <summary>
    /// Maps each older schema to the last mod version that can still migrate it; lore names this version when a
    /// cell is <see cref="SchemaGate.TooOld"/>.
    /// </summary>
    public static IReadOnlyDictionary<int, string> UpgradePath { get; } = new Dictionary<int, string>
    {
        [1] = StripReleaseVersion
    };

    /// <summary>Classifies a recorded schema against this version's <see cref="Current"/> schema.</summary>
    public static SchemaGate Classify(int schema) => Classify(schema, Current);

    /// <summary>Classifies a recorded schema against a given current schema.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The current schema is not positive.</exception>
    public static SchemaGate Classify(int schema, int current)
    {
        if (current < 1) throw new ArgumentOutOfRangeException(nameof(current), current, "A schema is positive.");
        if (schema < 1) return SchemaGate.Invalid;
        if (schema == current) return SchemaGate.Current;
        if (schema > current) return SchemaGate.TooNew;
        return schema == current - 1 ? SchemaGate.Migrate : SchemaGate.TooOld;
    }
}
