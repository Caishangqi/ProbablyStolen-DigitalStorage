using System;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

/// <summary>
/// The log categories of the mod's own free-text records, each of which players can turn off and filter by level in
/// the mod settings. Only the startup and shutdown status lines bypass them, see <see cref="PlatformComposition"/>.
/// </summary>
internal sealed class PlatformLogs
{
    internal PlatformLogs(DiagnosticLogger logger, BuiltinLogCategories categories)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));
        if (categories == null) throw new ArgumentNullException(nameof(categories));
        Storage = new CategoryLog(logger, categories.Storage);
        Transfer = new CategoryLog(logger, categories.Transfer);
        Comp = new CategoryLog(logger, categories.Comp);
        Content = new CategoryLog(logger, categories.Content);
        Network = new CategoryLog(logger, categories.Network);
        Ui = new CategoryLog(logger, categories.Ui);
    }

    /// <summary>
    /// Gets the logs of the running composition, for static and patch code that cannot be handed them; null outside a
    /// session, when such records are dropped.
    /// </summary>
    internal static PlatformLogs? Current { get; private set; }

    /// <summary>Makes <paramref name="logs"/> the current logs, or with null clears them.</summary>
    internal static void Activate(PlatformLogs? logs) => Current = logs;

    /// <summary>Stored cells, their pages and item identities.</summary>
    internal CategoryLog Storage { get; }

    /// <summary>Terminal and IO Port moves.</summary>
    internal CategoryLog Transfer { get; }

    /// <summary>Other mods: integrations and the public API.</summary>
    internal CategoryLog Comp { get; }

    /// <summary>Registered items, vendors, customers, network upgrades and item content.</summary>
    internal CategoryLog Content { get; }

    /// <summary>Network scans, power and the storage event bus.</summary>
    internal CategoryLog Network { get; }

    /// <summary>Windows, tooltips, search and settings; window traces are verbose.</summary>
    internal CategoryLog Ui { get; }
}
