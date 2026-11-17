using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;

/// <summary>
/// The storage operations of one terminal, created once per terminal item and shared by every storage view built for
/// it. All item movement goes through these delegates; the view never touches native inventories itself.
/// </summary>
internal sealed class StorageTerminalServices
{
    internal StorageTerminalServices(ModText text, ITerminalViewSettings settings,
        Func<TerminalInventorySnapshot> readSnapshot, Func<TerminalEntry, long, TerminalExtractionResult> extract,
        Func<TerminalDragBatch, InventoryDropTarget, TerminalExtractionResult> extractBatch,
        IReadOnlyList<IEntryAttributeCapture> attributeCaptures,
        Func<IReadOnlyList<GameItem>, Action<string>, TerminalInsertionResult> storeSelection,
        Func<GameItem, int, Action<string>, TerminalInsertionResult> storeSingle,
        Func<GameItem, int, bool> canStore, Func<ItemKey, GameItem?> findSample,
        Func<bool> diagnosticsEnabled, Func<bool> identityLogEnabled, Action<string> writeDiagnostic,
        Func<Action<NetworkChanged>, IDisposable> subscribeNetwork, TerminalSearchService search)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ReadSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
        Extract = extract ?? throw new ArgumentNullException(nameof(extract));
        ExtractBatch = extractBatch ?? throw new ArgumentNullException(nameof(extractBatch));
        AttributeCaptures = attributeCaptures ?? throw new ArgumentNullException(nameof(attributeCaptures));
        StoreSelection = storeSelection ?? throw new ArgumentNullException(nameof(storeSelection));
        StoreSingle = storeSingle ?? throw new ArgumentNullException(nameof(storeSingle));
        CanStore = canStore ?? throw new ArgumentNullException(nameof(canStore));
        FindSample = findSample ?? throw new ArgumentNullException(nameof(findSample));
        DiagnosticsEnabled = diagnosticsEnabled ?? throw new ArgumentNullException(nameof(diagnosticsEnabled));
        IdentityLogEnabled = identityLogEnabled ?? throw new ArgumentNullException(nameof(identityLogEnabled));
        WriteDiagnostic = writeDiagnostic ?? throw new ArgumentNullException(nameof(writeDiagnostic));
        SubscribeNetwork = subscribeNetwork ?? throw new ArgumentNullException(nameof(subscribeNetwork));
        Search = search ?? throw new ArgumentNullException(nameof(search));
    }

    internal ModText Text { get; }
    internal ITerminalViewSettings Settings { get; }
    internal Func<TerminalInventorySnapshot> ReadSnapshot { get; }
    /// <summary>Takes up to the given pieces of one entry into the player's inventory.</summary>
    internal Func<TerminalEntry, long, TerminalExtractionResult> Extract { get; }
    /// <summary>Moves a validated display batch to its target after release-time checks.</summary>
    internal Func<TerminalDragBatch, InventoryDropTarget, TerminalExtractionResult> ExtractBatch { get; }
    /// <summary>Reads named attributes from samples for an active storage projector.</summary>
    internal IReadOnlyList<IEntryAttributeCapture> AttributeCaptures { get; }
    internal Func<IReadOnlyList<GameItem>, Action<string>, TerminalInsertionResult> StoreSelection { get; }
    internal Func<GameItem, int, Action<string>, TerminalInsertionResult> StoreSingle { get; }
    internal Func<GameItem, int, bool> CanStore { get; }
    internal Func<ItemKey, GameItem?> FindSample { get; }
    internal Func<bool> DiagnosticsEnabled { get; }

    /// <summary>Gets whether clicked slots write their full item identity to the log.</summary>
    internal Func<bool> IdentityLogEnabled { get; }
    internal Action<string> WriteDiagnostic { get; }
    internal Func<Action<NetworkChanged>, IDisposable> SubscribeNetwork { get; }
    internal TerminalSearchService Search { get; }

    /// <summary>
    /// The last search text of this terminal. It outlives the window, so reopening shows the same search.
    /// </summary>
    internal string SearchText { get; set; } = string.Empty;
}
