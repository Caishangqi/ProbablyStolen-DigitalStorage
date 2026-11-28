using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Common.Application.Events;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Integration;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;

/// <summary>
/// The storage operations of one terminal, created once per terminal item and shared by every storage view built for
/// it. All item movement goes through these delegates; the view never touches native inventories itself.
/// </summary>
internal sealed class StorageTerminalServices
{
    private readonly CategoryLog _identityLog;

    internal StorageTerminalServices(ModText text, ITerminalViewSettings settings,
        Func<TerminalInventorySnapshot> readSnapshot, Func<TerminalEntry, long, TransferOperation, TerminalExtractionResult> extract,
        Func<TerminalDragBatch, InventoryDropTarget, TransferOperation, TerminalExtractionResult> extractBatch,
        IReadOnlyList<IEntryAttributeCapture> attributeCaptures,
        Func<IReadOnlyList<GameItem>, TransferOperation, TerminalInsertionResult> storeSelection,
        Func<GameItem, int, TransferOperation, TerminalInsertionResult> storeSingle,
        Func<IReadOnlyList<GameItem>, int?, IReadOnlyList<string?>> storeRefusals, Func<ItemKey, GameItem?> findSample,
        Func<ItemKey, GameItem?> findPreviewSample, Func<GameItemElement, RichText> renderStoredTooltip,
        CategoryLog log, Func<bool> identityLogEnabled, CategoryLog identityLog,
        Func<Action<NetworkChanged>, IDisposable> subscribeNetwork, TerminalSearchService search, TransferDiagnostics transfers,
        IntegrationDecorationCatalog decorations, string modId)
    {
        ModId = modId;
        Transfers = transfers;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ReadSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
        Extract = extract ?? throw new ArgumentNullException(nameof(extract));
        ExtractBatch = extractBatch ?? throw new ArgumentNullException(nameof(extractBatch));
        AttributeCaptures = attributeCaptures ?? throw new ArgumentNullException(nameof(attributeCaptures));
        StoreSelection = storeSelection ?? throw new ArgumentNullException(nameof(storeSelection));
        StoreSingle = storeSingle ?? throw new ArgumentNullException(nameof(storeSingle));
        StoreRefusals = storeRefusals ?? throw new ArgumentNullException(nameof(storeRefusals));
        FindSample = findSample ?? throw new ArgumentNullException(nameof(findSample));
        FindPreviewSample = findPreviewSample ?? throw new ArgumentNullException(nameof(findPreviewSample));
        RenderStoredTooltip = renderStoredTooltip ?? throw new ArgumentNullException(nameof(renderStoredTooltip));
        Log = log ?? throw new ArgumentNullException(nameof(log));
        IdentityLogEnabled = identityLogEnabled ?? throw new ArgumentNullException(nameof(identityLogEnabled));
        _identityLog = identityLog ?? throw new ArgumentNullException(nameof(identityLog));
        SubscribeNetwork = subscribeNetwork ?? throw new ArgumentNullException(nameof(subscribeNetwork));
        Search = search ?? throw new ArgumentNullException(nameof(search));
        Decorations = decorations ?? throw new ArgumentNullException(nameof(decorations));
    }

    internal string ModId { get; }
    internal TransferDiagnostics Transfers { get; }
    internal ModText Text { get; }
    internal ITerminalViewSettings Settings { get; }
    internal Func<TerminalInventorySnapshot> ReadSnapshot { get; }
    /// <summary>Takes up to the given pieces of one entry into the player's inventory.</summary>
    internal Func<TerminalEntry, long, TransferOperation, TerminalExtractionResult> Extract { get; }
    /// <summary>Moves a validated display batch to its target after release-time checks.</summary>
    internal Func<TerminalDragBatch, InventoryDropTarget, TransferOperation, TerminalExtractionResult> ExtractBatch { get; }
    /// <summary>Reads named attributes from samples for an active storage projector.</summary>
    internal IReadOnlyList<IEntryAttributeCapture> AttributeCaptures { get; }
    internal Func<IReadOnlyList<GameItem>, TransferOperation, TerminalInsertionResult> StoreSelection { get; }
    internal Func<GameItem, int, TransferOperation, TerminalInsertionResult> StoreSingle { get; }
    /// <summary>
    /// Gets why each dragged item could not be stored as a text key, or null when it could, reading the network once.
    /// The amount is the single drag's quantity; null for a group drag, which moves whole stacks.
    /// </summary>
    internal Func<IReadOnlyList<GameItem>, int?, IReadOnlyList<string?>> StoreRefusals { get; }
    internal Func<ItemKey, GameItem?> FindSample { get; }
    internal Func<ItemKey, GameItem?> FindPreviewSample { get; }
    internal Func<GameItemElement, RichText> RenderStoredTooltip { get; }
    /// <summary>Gets the interface log the terminal windows write to.</summary>
    internal CategoryLog Log { get; }

    /// <summary>Gets whether clicked slots write their full item identity to the log.</summary>
    internal Func<bool> IdentityLogEnabled { get; }

    /// <summary>Writes a trace of the terminal, at the interface log's verbose level.</summary>
    internal void WriteDiagnostic(string message) => Log.Verbose("terminal_trace", message);

    /// <summary>Writes a terminal problem a player may need to report.</summary>
    internal void WriteWarning(string message) => Log.Warning("terminal_warning", message);

    /// <summary>
    /// Writes a full item identity, values included, to the storage log at its verbose level. Callers check the
    /// identity log setting first: ordinary logs never carry item data.
    /// </summary>
    internal void WriteIdentity(string text) => _identityLog.Verbose("item_identity", text);
    internal Func<Action<NetworkChanged>, IDisposable> SubscribeNetwork { get; }
    internal TerminalSearchService Search { get; }
    internal IntegrationDecorationCatalog Decorations { get; }

    /// <summary>
    /// The last search text of this terminal. It outlives the window, so reopening shows the same search.
    /// </summary>
    internal string SearchText { get; set; } = string.Empty;
}
