using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;

/// <summary>
/// Runs the registered storage extensions for one window: picks the projector, collects slot overlays and tooltip
/// panels, and keeps a failing extension from breaking the terminal. The storage view only talks to this class.
/// </summary>
public sealed class StoragePresentationPipeline
{
    private readonly IReadOnlyList<IStorageEntryProjector> _projectors;
    private readonly IReadOnlyList<ISlotDecorator> _decorators;
    private readonly IReadOnlyList<IEntryTooltipContributor> _contributors;
    private readonly IReadOnlyDictionary<string, TooltipPanelDefinition> _panels;
    private readonly Action<string> _diagnostic;

    /// <summary>Creates a pipeline over registered extensions.</summary>
    public StoragePresentationPipeline(IEnumerable<IStorageEntryProjector> projectors,
        IEnumerable<ISlotDecorator> decorators, IEnumerable<IEntryTooltipContributor> contributors,
        IEnumerable<TooltipPanelDefinition> panels, Action<string> diagnostic)
    {
        _projectors = (projectors ?? throw new ArgumentNullException(nameof(projectors))).ToArray();
        _decorators = (decorators ?? throw new ArgumentNullException(nameof(decorators)))
            .OrderBy(decorator => decorator.Order).ThenBy(decorator => decorator.Id, StringComparer.Ordinal).ToArray();
        _contributors = (contributors ?? throw new ArgumentNullException(nameof(contributors)))
            .OrderBy(contributor => contributor.Order).ThenBy(contributor => contributor.Id, StringComparer.Ordinal).ToArray();
        var byId = new Dictionary<string, TooltipPanelDefinition>(StringComparer.Ordinal)
        {
            [TooltipPanelDefinition.HintId] = TooltipPanelDefinition.Hint
        };
        foreach (var panel in panels ?? throw new ArgumentNullException(nameof(panels))) byId[panel.Id] = panel;
        _panels = byId;
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Gets a pipeline without extensions: identity projection, no overlays, no added lines.</summary>
    public static StoragePresentationPipeline Empty { get; } = new(Array.Empty<IStorageEntryProjector>(),
        Array.Empty<ISlotDecorator>(), Array.Empty<IEntryTooltipContributor>(), Array.Empty<TooltipPanelDefinition>(),
        _ => { });

    /// <summary>Gets the storage view's own panel definition.</summary>
    public TooltipPanelDefinition HintPanel => _panels[TooltipPanelDefinition.HintId];

    /// <summary>Gets a number that changes whenever cached groups, marks or lines must be rebuilt.</summary>
    public long Revision { get; private set; }

    /// <summary>Asks for a rebuild, for example after settings were saved or modules changed.</summary>
    public void Invalidate() => Revision++;

    /// <summary>
    /// Gets the active projector, or null. When more than one is active none is used, so the result never depends on
    /// registration order.
    /// </summary>
    public IStorageEntryProjector? ActiveProjector(TerminalContext? context)
    {
        if (context == null) return null;
        var active = _projectors.Where(projector => Guard(projector.Id, "projector", () => projector.IsActive(context), false))
            .ToArray();
        if (active.Length <= 1) return active.FirstOrDefault();
        _diagnostic($"storage_pipeline refused=projectors ids={string.Join(",", active.Select(projector => projector.Id))} reason=several_active");
        return null;
    }

    /// <summary>Groups entries with the active projector, or one group per entry when none is usable.</summary>
    public IReadOnlyList<ProjectedGroup> Project(IReadOnlyList<TerminalEntry> entries, TerminalContext? context,
        EntryAttributes attributes)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        var projector = ActiveProjector(context);
        if (projector == null) return IdentityProjection.Project(entries);
        var groups = Guard(projector.Id, "projector", () => projector.Project(entries, context!, attributes), null);
        if (groups != null && IsPartition(entries, groups)) return groups;
        if (groups != null) _diagnostic($"storage_pipeline refused=projector id={projector.Id} reason=invalid_groups");
        return IdentityProjection.Project(entries);
    }

    /// <summary>Collects overlays in slot order, with external overlays above the registered rules.</summary>
    /// <param name="page">The exact page whose slots are being decorated.</param>
    /// <param name="context">The current terminal context.</param>
    /// <param name="external">One list per slot, or null when no external results are available.</param>
    public IReadOnlyList<IReadOnlyList<SlotDecoration>> DecoratePage(StorageScreenPage page, TerminalContext? context,
        IReadOnlyList<IReadOnlyList<SlotDecoration>>? external = null)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));
        if (external != null && external.Count != page.Slots.Count)
            throw new ArgumentException("External decorations must align with the page slots.", nameof(external));

        var result = new IReadOnlyList<SlotDecoration>[page.Slots.Count];
        foreach (var slot in page.Slots)
        {
            if (slot.Group is not { Total: > 0 } group)
            {
                result[slot.Index] = Array.Empty<SlotDecoration>();
                continue;
            }

            var builtIn = Decorate(group, context);
            var added = external?[slot.Index];
            result[slot.Index] = added is not { Count: > 0 } ? builtIn
                : builtIn.Count == 0 ? added : builtIn.Concat(added).ToArray();
        }
        return result;
    }

    /// <summary>Collects the overlays of one slot, bottom first: decorators by order, each in the order it added them.</summary>
    public IReadOnlyList<SlotDecoration> Decorate(DisplayEntry entry, TerminalContext? context)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (context == null || _decorators.Count == 0) return Array.Empty<SlotDecoration>();
        List<SlotDecoration>? decorations = null;
        foreach (var decorator in _decorators)
        {
            var sink = new SlotDecorationSink();
            var ran = Guard(decorator.Id, "decorator", () =>
            {
                if (!decorator.IsActive(context)) return false;
                decorator.Decorate(entry, context, sink);
                return true;
            }, false);
            if (ran && sink.Items.Count > 0) (decorations ??= new List<SlotDecoration>()).AddRange(sink.Items);
        }

        return decorations ?? (IReadOnlyList<SlotDecoration>)Array.Empty<SlotDecoration>();
    }

    /// <summary>Collects tooltip lines for one slot.</summary>
    public IReadOnlyList<TooltipPanel> Tooltip(DisplayEntry entry, TerminalContext? context, EntryAttributes? attributes = null)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        return BuildTooltip(entry, context, null, attributes ?? EntryAttributes.Empty);
    }

    /// <summary>Collects tooltip lines for one slot and its current retained selection.</summary>
    public IReadOnlyList<TooltipPanel> TooltipForSelection(DisplayEntry entry, TerminalContext? context,
        IReadOnlyList<DisplayEntry> selection, EntryAttributes attributes)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (selection == null) throw new ArgumentNullException(nameof(selection));
        if (attributes == null) throw new ArgumentNullException(nameof(attributes));
        return BuildTooltip(entry, context, selection, attributes);
    }

    private IReadOnlyList<TooltipPanel> BuildTooltip(DisplayEntry entry, TerminalContext? context,
        IReadOnlyList<DisplayEntry>? selection, EntryAttributes attributes)
    {
        if (context == null || _contributors.Count == 0) return Array.Empty<TooltipPanel>();
        var lines = new Dictionary<string, List<TooltipLine>>(StringComparer.Ordinal);
        foreach (var contributor in _contributors)
        {
            var sink = new EntryTooltipSink();
            var ran = Guard(contributor.Id, "tooltip", () =>
            {
                if (!contributor.IsActive(context)) return false;
                contributor.Contribute(entry, context, attributes, sink);
                return true;
            }, false);
            if (ran) AddTooltipLines(contributor.Id, sink, lines);
        }

        if (selection is { Count: > 1 })
        {
            foreach (var contributor in _contributors.OfType<ISelectionTooltipContributor>())
            {
                var sink = new EntryTooltipSink();
                var ran = Guard(contributor.Id, "selection_tooltip", () =>
                {
                    if (!contributor.IsActive(context)) return false;
                    contributor.ContributeSelection(entry, selection, context, attributes, sink);
                    return true;
                }, false);
                if (ran) AddTooltipLines(contributor.Id, sink, lines);
            }
        }

        return lines.Select(pair => new TooltipPanel(_panels[pair.Key], pair.Value))
            .OrderBy(panel => panel.Definition.Order).ThenBy(panel => panel.Definition.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private void AddTooltipLines(string contributorId, EntryTooltipSink sink,
        Dictionary<string, List<TooltipLine>> lines)
    {
        foreach (var line in sink.Lines)
        {
            if (!_panels.ContainsKey(line.PanelId))
            {
                _diagnostic($"storage_pipeline skipped=tooltip id={contributorId} panel={line.PanelId} reason=unknown_panel");
                continue;
            }

            if (!lines.TryGetValue(line.PanelId, out var list)) lines.Add(line.PanelId, list = new List<TooltipLine>());
            list.Add(line);
        }
    }

    private static bool IsPartition(IReadOnlyList<TerminalEntry> entries, IReadOnlyList<ProjectedGroup> groups)
    {
        var expected = new HashSet<TerminalEntry>(entries);
        var seen = new HashSet<TerminalEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group == null || group.Members.Count == 0 || !ids.Add(group.Id)) return false;
            var identifier = group.Members[0]?.Key.Identifier;
            foreach (var member in group.Members)
            {
                if (member == null || !expected.Contains(member) || !seen.Add(member) ||
                    member.Key.Identifier != identifier) return false;
            }
        }

        return seen.Count == expected.Count;
    }

    // Extensions are mod code the terminal does not control; a failure skips that extension instead of the view.
    private T Guard<T>(string id, string kind, Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception error)
        {
            _diagnostic($"storage_pipeline failed={kind} id={id} error={error.GetType().Name}");
            return fallback;
        }
    }
}
