using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>One online/offline storage snapshot for the terminal view.</summary>
public sealed class TerminalInventorySnapshot
{
    /// <summary>Creates an immutable terminal snapshot.</summary>
    public TerminalInventorySnapshot(bool online, IEnumerable<TerminalEntry> entries,
        bool storageUnavailable = false)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        Online = online;
        StorageUnavailable = storageUnavailable;
        Entries = entries.Where(entry => entry != null).ToArray();
    }

    /// <summary>Gets whether the network is available.</summary>
    public bool Online { get; }

    /// <summary>Gets whether mounted content failed validation instead of being empty.</summary>
    public bool StorageUnavailable { get; }

    /// <summary>Gets the immutable display entries.</summary>
    public IReadOnlyList<TerminalEntry> Entries { get; }
}
