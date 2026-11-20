using System;

namespace ProbablyStolen.DigitalStorage.Api.Client.Tooltips;

/// <summary>Tracks temporary lore written by a compatibility patch during one synchronous terminal tooltip.</summary>
internal sealed class TerminalTooltipScope
{
    [ThreadStatic] private static RenderFrame? _current;

    private readonly GameItem _item;
    private readonly string _initialDescription;
    private readonly string _initialFlavor;
    private LoreWrite _description;
    private LoreWrite _flavor;
    private bool _observed;

    private TerminalTooltipScope(GameItem item)
    {
        _item = item;
        _initialDescription = item.shortDescription;
        _initialFlavor = item.flavorText;
    }

    internal static TerminalTooltipScope? For(GameItem? item) =>
        _current is { Scope: var scope } && ReferenceEquals(scope._item, item) ? scope : null;

    internal static TResult Render<TResult>(GameItem item, Func<TResult> render, Action onLoreChanged)
    {
        var previous = _current;
        var existing = For(item);
        // Reuse the restoration boundary, but keep each invocation in the frame chain.
        for (var frame = previous; existing == null && frame != null; frame = frame.Parent)
        {
            if (ReferenceEquals(frame.Scope._item, item)) existing = frame.Scope;
        }

        var scope = existing ?? new TerminalTooltipScope(item);
        _current = new RenderFrame(scope, previous);
        try
        {
            return render();
        }
        finally
        {
            _current = previous;
            if (existing == null && scope.RestoreLore()) onLoreChanged();
        }
    }

    private sealed class RenderFrame
    {
        internal RenderFrame(TerminalTooltipScope scope, RenderFrame? parent) => (Scope, Parent) = (scope, parent);

        internal TerminalTooltipScope Scope { get; }
        internal RenderFrame? Parent { get; }
    }

    internal void Record(string descriptionBefore, string flavorBefore)
    {
        _observed = true;
        _description.Record(descriptionBefore, _item.shortDescription);
        _flavor.Record(flavorBefore, _item.flavorText);
    }

    private bool RestoreLore()
    {
        if (!_observed) return false;
        if (_description.ShouldRestore(_item.shortDescription)) _item.shortDescription = _description.Original!;
        if (_flavor.ShouldRestore(_item.flavorText)) _item.flavorText = _flavor.Original!;
        return !Same(_item.shortDescription, _initialDescription) || !Same(_item.flavorText, _initialFlavor);
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

    private struct LoreWrite
    {
        private bool _recorded;
        private string? _written;
        internal string? Original { get; private set; }

        internal void Record(string before, string after)
        {
            if (Same(before, after)) return;
            if (!_recorded || !Same(before, _written)) Original = before;
            _recorded = true;
            _written = after;
        }

        internal bool ShouldRestore(string current) => _recorded && Same(current, _written);
    }
}
