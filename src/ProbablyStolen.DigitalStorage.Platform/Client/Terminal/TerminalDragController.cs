using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

internal enum TerminalDragRegion { Other, Content, Slot }
internal enum TerminalDragPhase { Idle, Waiting, Selecting, Selected, DraggingSingle, DraggingSelection }
internal delegate bool TerminalDragResolveTarget(PointerPoint point, out InventoryDropTarget? target,
    out TerminalDragTargetState state);

internal sealed class TerminalDragController
{
    private readonly DragSelectionModel<string> _selection = new();
    private readonly DragAmountModel _amount = new();
    private readonly Func<bool> _session;
    private readonly Func<StorageScreenPage> _currentPage;
    private readonly Func<long> _presentationRevision;
    private readonly Func<StorageScreenPage, StorageScreenPage, IEnumerable<string>, long, long?, TerminalDragBatch?> _createBatch;
    private readonly TerminalDragResolveTarget _resolve;
    private readonly Func<TerminalDragBatch, InventoryDropTarget, TerminalExtractionResult> _extract;
    private readonly Action<TerminalExtractionResult?, Exception?> _refresh;
    private readonly Action<IReadOnlyList<string>> _highlight;
    private readonly Action<PointerRectangle?> _band;
    private readonly Action<PointerPoint, string?> _ghost;
    private readonly Action _hideGhost;
    private readonly Action<TerminalDragTargetState, InventoryDropTarget?> _feedback;
    // Restricts gesture ownership at press; an owned gesture may move outside this area.
    private readonly Func<PointerPoint, bool>? _gestureArea;
    private StorageScreenPage? _source;
    private IReadOnlyList<DragSelectionCandidate<string>> _candidates = Array.Empty<DragSelectionCandidate<string>>();
    private IReadOnlyList<string> _selected = Array.Empty<string>();
    private PointerPoint _origin;
    private PointerPoint? _lastDragPoint;
    private PointerPoint? _lastBandPoint;
    private string? _pressedId;
    private float _threshold;
    private double _wheelRemainder;
    private bool _owned;
    private bool _retainedSelection;
    private bool _dragSelection;
    private StorageScreenPage? _previewPage;
    private long? _previewAmount;
    private TerminalDragBatch? _previewBatch;

    internal TerminalDragController(Func<bool> session, Func<StorageScreenPage> currentPage,
        Func<long> presentationRevision,
        Func<StorageScreenPage, StorageScreenPage, IEnumerable<string>, long, long?, TerminalDragBatch?> createBatch,
        TerminalDragResolveTarget resolve, Func<TerminalDragBatch, InventoryDropTarget, TerminalExtractionResult> extract,
        Action<TerminalExtractionResult?, Exception?> refresh, Action<IReadOnlyList<string>> highlight,
        Action<PointerRectangle?> band, Action<PointerPoint, string?> ghost, Action hideGhost,
        Action<TerminalDragTargetState, InventoryDropTarget?> feedback, Func<PointerPoint, bool>? gestureArea = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _currentPage = currentPage ?? throw new ArgumentNullException(nameof(currentPage));
        _presentationRevision = presentationRevision ?? throw new ArgumentNullException(nameof(presentationRevision));
        _createBatch = createBatch ?? throw new ArgumentNullException(nameof(createBatch));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _extract = extract ?? throw new ArgumentNullException(nameof(extract));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _highlight = highlight ?? throw new ArgumentNullException(nameof(highlight));
        _band = band ?? throw new ArgumentNullException(nameof(band));
        _ghost = ghost ?? throw new ArgumentNullException(nameof(ghost));
        _hideGhost = hideGhost ?? throw new ArgumentNullException(nameof(hideGhost));
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _gestureArea = gestureArea;
    }

    internal TerminalDragPhase Phase { get; private set; }
    internal bool OwnsGesture => _owned;
    // Unity may dispatch its click before the native release callback ends an unconfirmed press.
    internal bool AllowsPrimaryClick => _owned && Phase == TerminalDragPhase.Waiting && IsCurrent();
    internal IReadOnlyList<string> SelectedIds => _selected;
    internal IReadOnlyList<DragSelectionCandidate<string>> Candidates => _candidates;
    internal PointerPoint Origin => _origin;
    internal bool PausesResort => _owned && !_dragSelection &&
        Phase is TerminalDragPhase.Waiting or TerminalDragPhase.DraggingSingle;
    internal bool PreviewUsesAnchor => Phase == TerminalDragPhase.DraggingSingle &&
        _amount.RequestedPieces == 1;

    internal bool Press(PointerPoint point, TerminalDragRegion region, string? slotId, StorageScreenPage page,
        IReadOnlyList<DragSelectionCandidate<string>> candidates, float? nativeThreshold, bool allPieces = false)
    {
        if (_owned || !point.IsValid || (_gestureArea != null && !_gestureArea(point)) || page == null || candidates == null || !_session() ||
            region == TerminalDragRegion.Other || page.ViewRevision != _currentPage().ViewRevision ||
            page.PresentationRevision != _currentPage().PresentationRevision ||
            page.PresentationRevision != _presentationRevision()) return false;
        if (_source != null && !Matches(page)) Cancel();
        if (region == TerminalDragRegion.Slot)
        {
            var group = page.Slots.FirstOrDefault(slot => slot.Group?.Id == slotId)?.Group;
            if (group == null || group.Total <= 0 || !candidates.Any(candidate => candidate.Id == slotId)) return false;
            _dragSelection = _retainedSelection && _selected.Contains(group.Id);
            if (!_dragSelection)
            {
                _selection.Cancel();
                _retainedSelection = false;
                _selected = new[] { group.Id };
                _amount.Begin(group.Total, allPieces);
            }
            _pressedId = group.Id;
            Phase = TerminalDragPhase.Waiting;
            _highlight(_selected);
        }
        else if (region == TerminalDragRegion.Content)
        {
            _selection.Cancel();
            _retainedSelection = false;
            _selected = Array.Empty<string>();
            _highlight(_selected);
            if (!_selection.Begin(point, page.ContentRevision)) return false;
            _pressedId = null;
            _lastBandPoint = point;
            Phase = TerminalDragPhase.Selecting;
        }
        else return false;
        _source = page;
        _candidates = candidates.ToArray();
        _origin = point;
        _threshold = nativeThreshold.HasValue && nativeThreshold.Value >= 0 &&
            !float.IsNaN(nativeThreshold.Value) && !float.IsInfinity(nativeThreshold.Value)
            ? nativeThreshold.Value : float.PositiveInfinity;
        _wheelRemainder = 0;
        _owned = true;
        return true;
    }

    internal void Move(PointerPoint point)
    {
        if (!_owned) return;
        if (!point.IsValid || !IsCurrent(point)) { Cancel(); return; }
        if (Phase == TerminalDragPhase.Selecting)
        {
            _selected = _selection.Update(point, _candidates, _source!.ContentRevision);
            if (!_selection.IsActive) { Cancel(); return; }
            _lastBandPoint = point;
            _highlight(_selected);
            if (PointerRectangle.TryCreate(_origin, point, out var rectangle)) _band(rectangle);
            return;
        }
        if (Phase == TerminalDragPhase.Waiting)
        {
            var dx = (double)point.X - _origin.X;
            var dy = (double)point.Y - _origin.Y;
            if (float.IsInfinity(_threshold) || dx * dx + dy * dy < (double)_threshold * _threshold) return;
            Phase = _dragSelection ? TerminalDragPhase.DraggingSelection : TerminalDragPhase.DraggingSingle;
        }
        if (Phase != TerminalDragPhase.DraggingSingle && Phase != TerminalDragPhase.DraggingSelection) return;
        if (Phase == TerminalDragPhase.DraggingSingle && !ClampAmount()) { Cancel(); return; }
        _lastDragPoint = point;
        UpdateGhost(point);
        UpdateTargetFeedback(point);
    }

    private void UpdateGhost(PointerPoint point) => _ghost(point, Phase == TerminalDragPhase.DraggingSingle
        ? _amount.RequestedPieces.ToString(CultureInfo.InvariantCulture) : null);

    internal TerminalDragBatch? CreatePreviewBatch()
    {
        if (Phase != TerminalDragPhase.DraggingSingle && Phase != TerminalDragPhase.DraggingSelection) return null;
        var page = _currentPage();
        if (!_session() || !Matches(page)) return null;
        long? amount = Phase == TerminalDragPhase.DraggingSingle ? _amount.RequestedPieces : null;
        if (ReferenceEquals(_previewPage, page) && _previewAmount == amount) return _previewBatch;

        _previewBatch = _createBatch(_source!, page, _selected, _presentationRevision(), amount);
        _previewPage = page;
        _previewAmount = amount;
        return _previewBatch;
    }

    private void UpdateTargetFeedback(PointerPoint point)
    {
        _resolve(point, out var target, out var state);
        if (state == TerminalDragTargetState.Pending && PreviewUsesAnchor && target?.IsAnchorValid == false)
            state = TerminalDragTargetState.Rejected;
        _feedback(state, target);
    }

    internal void Wheel(float steps, PointerPoint point)
    {
        if (Phase != TerminalDragPhase.DraggingSingle || float.IsNaN(steps) || float.IsInfinity(steps)) return;
        _wheelRemainder += steps;
        var whole = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Truncate(_wheelRemainder)));
        _wheelRemainder -= whole;
        if (!IsCurrent(point) || !TryAvailable(out var available)) { Cancel(); return; }
        _amount.ApplyWheel(whole, available);
        Move(point);
    }

    /// <summary>
    /// Follows a scroll during a band: the fixed corner moves with the content it was pressed on, and the candidates
    /// cover every row the band has scrolled through, so rows that left the view stay selected.
    /// </summary>
    internal void ScrollSelection(PointerPoint origin, IReadOnlyList<DragSelectionCandidate<string>> candidates)
    {
        if (!_owned || Phase != TerminalDragPhase.Selecting || candidates == null) return;
        if (!_selection.MoveOrigin(origin)) { Cancel(); return; }
        _origin = origin;
        _candidates = candidates.ToArray();
        if (_lastBandPoint is { } point) Move(point);
    }

    internal void Halve(PointerPoint point)
    {
        if (Phase != TerminalDragPhase.DraggingSingle) return;
        if (!IsCurrent(point) || !TryAvailable(out var available)) { Cancel(); return; }
        _amount.Halve(available);
        Move(point);
    }

    internal bool Release(PointerPoint point)
    {
        if (!point.IsValid || !IsCurrent(point))
        {
            var consume = _owned || Phase is TerminalDragPhase.Selecting or TerminalDragPhase.DraggingSingle
                or TerminalDragPhase.DraggingSelection;
            Cancel();
            return consume;
        }
        if (Phase == TerminalDragPhase.Selecting)
        {
            Move(point);
            if (!_owned) return true;
            _selected = _selection.Complete(_source!.ContentRevision);
            _lastBandPoint = null;
            _owned = false;
            _band(null);
            _retainedSelection = _selected.Count > 0;
            Phase = _retainedSelection ? TerminalDragPhase.Selected : TerminalDragPhase.Idle;
            if (_selected.Count == 0) _source = null;
            return true;
        }
        if (Phase == TerminalDragPhase.Waiting && _dragSelection)
        {
            _owned = false;
            _amount.End();
            Phase = TerminalDragPhase.Selected;
            return false;
        }
        if (Phase != TerminalDragPhase.DraggingSingle && Phase != TerminalDragPhase.DraggingSelection)
        {
            Cancel();
            return false;
        }
        var single = Phase == TerminalDragPhase.DraggingSingle;
        _owned = false;
        try
        {
            if (!_resolve(point, out var target, out var state) || state != TerminalDragTargetState.Pending ||
                target == null || !IsCurrent(point) || (single && !ClampAmount())) return true;
            var page = _currentPage();
            var batch = _createBatch(_source!, page, _selected, _presentationRevision(),
                single ? _amount.RequestedPieces : null);
            if (batch == null || !IsCurrent(point)) return true;
            if (PreviewUsesAnchor && !target.IsAnchorValid) return true;
            if (!PreviewUsesAnchor) target = target.WithoutAnchor();
            TerminalExtractionResult? result = null;
            Exception? error = null;
            try { result = _extract(batch, target); }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException &&
                                      ex is not AccessViolationException)
            { error = ex; }
            finally { _refresh(result, error); }
        }
        finally { Cancel(); }
        return true;
    }

    internal void SynchronizePage()
    {
        if (_source == null) return;
        if (!IsCurrent() || !AreAllAvailable(_selected, _currentPage()))
        {
            Cancel();
            return;
        }
        var previousAmount = _amount.RequestedPieces;
        if (PausesResort && !ClampAmount())
        {
            Cancel();
            return;
        }
        _highlight(_selected);
        if (_owned && _lastDragPoint is { } point)
        {
            if (_amount.RequestedPieces != previousAmount) UpdateGhost(point);
            UpdateTargetFeedback(point);
        }
    }

    internal void Cancel()
    {
        _owned = false;
        _retainedSelection = false;
        _dragSelection = false;
        Phase = TerminalDragPhase.Idle;
        _source = null;
        _pressedId = null;
        _candidates = Array.Empty<DragSelectionCandidate<string>>();
        _selected = Array.Empty<string>();
        _selection.Cancel();
        _amount.End();
        _wheelRemainder = 0;
        _lastDragPoint = null;
        _lastBandPoint = null;
        _previewPage = null;
        _previewBatch = null;
        _previewAmount = null;
        _band(null);
        _hideGhost();
        _highlight(_selected);
        _feedback(TerminalDragTargetState.None, null);
    }

    // A kept selection can span every filtered group, so the check is one pass over the groups, not one per selection.
    private static bool AreAllAvailable(IReadOnlyList<string> selected, StorageScreenPage page)
    {
        if (selected.Count == 0) return true;
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in page.Entries)
        {
            if (group.Total > 0) available.Add(group.Id);
        }
        return selected.All(available.Contains);
    }

    private bool Matches(StorageScreenPage page) => _source != null &&
        _source.ContentRevision == page.ContentRevision && _source.PresentationRevision == page.PresentationRevision &&
        page.PresentationRevision == _presentationRevision();

    private bool IsCurrent() => _source != null && _session() && Matches(_currentPage());

    private bool IsCurrent(PointerPoint point) => point.IsValid && IsCurrent();

    private bool TryAvailable(out long available)
    {
        available = _currentPage().Slots.FirstOrDefault(slot => slot.Group?.Id == _pressedId)?.Group?.Total ?? 0;
        return available > 0;
    }

    private bool ClampAmount() => TryAvailable(out var available) && _amount.ApplyWheel(0, available) > 0;
}
