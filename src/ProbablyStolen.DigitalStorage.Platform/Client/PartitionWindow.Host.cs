using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Client.Workbench;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using ProbablyStolen.DigitalStorage.Platform.Client.Drag;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using TMPro;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed partial class PartitionWindow
{
    private readonly MemoryTerminalStore _currentViewStore = new();
    private bool _moduleSubscription;
    private PartitionRuleScreen? _ruleScreen;

    /// <summary>Gets whether the rule page is the page on top.</summary>
    private bool RuleScreenShown => _ruleScreen != null && IsTopScreen(_ruleScreen);

    protected override string HostWindowId => WindowId;
    protected override string HostTitleKey => "partition_title";
    protected override TerminalSize HostSize => _view.PartitionSize;
    // The partition overview has no search row; the frame fits its content once shown (FitHeight).
    protected override int HostHeight =>
        TerminalWindowMetrics.GetHeight(SlotMetrics, CountRows()) - TerminalWindowMetrics.GetSearchHeight(SlotMetrics) - 6;
    protected override GameItem? PositionOwner => _workbench;
    protected override ICurrentTerminalStore CreateCurrentTerminalStore() => _currentViewStore;

    // The partition window edits cell tags; it keeps no feature data on the item it was opened from.
    protected override ProbablyStolen.DigitalStorage.Client.Terminal.Extensions.ITerminalFeatureData CreateFeatureData() =>
        ProbablyStolen.DigitalStorage.Client.Terminal.Extensions.NoTerminalFeatureData.Instance;
    protected override bool SessionIsCurrent() => _session.IsActive && !SaveManager.IsDecoding;
    protected override void SubscribeHostSources()
    {
        if (_moduleSubscription) return;
        CellUpgradeInventory.ModulesChanged += OnModulesChanged;
        _moduleSubscription = true;
    }

    protected override void OnHostOpened()
    {
        if (!HasCurrentView)
        {
            CustomWindow.Close();
            return;
        }
        if (_detail == null) return;
        _ruleScreen = new PartitionRuleScreen(this);
        PushScreen(_ruleScreen);
    }

    protected override void OnHostClosed(bool actualClose)
    {
        if (actualClose && _moduleSubscription)
        {
            CellUpgradeInventory.ModulesChanged -= OnModulesChanged;
            _moduleSubscription = false;
        }
        if (actualClose)
        {
            _ruleTooltip.Dispose();
            _ruleHints.Clear();
            _session.End();
            _ruleScreen = null;
            _detail = null;
            _workbenchWindow = null;
            _dropped.Clear();
            if (ReferenceEquals(_current, this)) _current = null;
        }
        _dropdown?.Close();
        _dropdown = null;
        _window = null;
        _body = null;
        _grid = null;
        _scrollbar = null;
        _dropHighlight = null;
        _ghost?.Dispose();
        _ghost = null;
        _status = null;
        _count = null;
    }

    internal PartitionMode DraftMode => _draft.Mode;

    internal void ToggleMode()
    {
        if (_session.IsActive) _draft.ToggleMode();
    }

    internal void ClearPartitions()
    {
        if (!_session.IsActive || _detail != null) return;
        EndRuleDrag();
        _messageKey = null;
        _draft.Clear();
        _selected = -1;
        _scrollRow = 0;
        Render();
    }

    internal void AttachOverview(RectTransform container, CustomUIWindow window)
    {
        _window = window;
        var root = new CustomUIBuilder(CustomUIManager.Instance, window, container);
        root.BeginColumn(spacing: 0f);
        _body = root.CurrentContainer;
        root.End();
        root.BeginRow(Gap);
        var textStep = TerminalWindowMetrics.TextStep(SlotMetrics);
        root.AddLabel(string.Empty, "status");
        SetWidth(root.Last, BodyWidth - Gap - CountWidth);
        TerminalControls.GrowText(root.Last, textStep);
        _status = root.Last;
        root.AddLabel(string.Empty, "count");
        SetWidth(root.Last, CountWidth);
        TerminalControls.GrowText(root.Last, textStep);
        _count = root.Last;
        if (root.Last.text != null) root.Last.text.alignment = TextAlignmentOptions.TopRight;
        root.End();
        _dropdown = new PopupDropdown(window, _search.MatchesText);
        window.root.AddComponent<SampleDropHandler>().Initialize(window, InputShield, OnSample, IsGridSampleTarget, OwnsWheel);
        window.root.AddComponent<FrameHandler>().Initialize(Tick);
        _ghost?.Dispose();
        _ghost = new DragGhostView(window.Rect, _pictures, SlotMetrics);
        if (_opened.Repaired) Message("partition_repaired", _opened.DroppedRules);
        Render();
    }

    internal void SuspendOverview() => EndRuleDrag();

    internal void RestoreOverview() => Render();

    internal PartitionViewState StoreOverviewState() => new(_scrollRow);

    internal void RestoreOverviewState(PartitionViewState state)
    {
        _scrollRow = state.ScrollRow;
        Render();
    }

    internal void OpenRuleScreen(int slot)
    {
        if (_draft.At(slot) == null || !_session.IsActive) return;
        OpenDetail(slot);
        _ruleScreen = new PartitionRuleScreen(this);
        PushScreen(_ruleScreen);
    }

    internal void ReturnFromRuleScreen()
    {
        if (IsHostClosing) return;
        _ruleScreen = null;
        _detail = null;
        Render();
    }

    internal void RefreshDetailScreen()
    {
        var offset = _detailScroll != null && _detailScroll.content != null && _detailScroll.viewport != null
            ? Mathf.Max(0f, _detailScroll.content.rect.height - _detailScroll.viewport.rect.height) *
                (1f - _detailScroll.verticalNormalizedPosition) : 0f;
        if (_ruleScreen == null || !RefreshScreenContent(_ruleScreen))
        {
            Render();
            return;
        }
        RestoreDetailScroll(offset);
    }

    internal void RefreshActiveDetail()
    {
        PartitionViewRefreshCoordinator.Refresh(_detail != null, RuleScreenShown,
            Render, RefreshDetailScreen);
    }

    internal bool HandleOverviewBack()
    {
        var cancelledPress = _pointerBack.HasActivePress;
        if (cancelledPress)
        {
            _pointerBack.Cancel();
            EndRuleDrag();
        }

        if (_dropdown?.IsOpen == true)
        {
            _dropdown.Close();
            return true;
        }

        return cancelledPress;
    }

    internal bool HandleRuleScreenBack()
    {
        if (_dropdown?.IsOpen == true)
        {
            _dropdown.Close();
            return true;
        }

        PopScreen();
        return true;
    }

    internal bool BuildRuleScreen(CustomUIBuilder builder)
    {
        BuildDetail(builder);
        return true;
    }

}

internal readonly struct PartitionViewState
{
    internal PartitionViewState(int scrollRow) => ScrollRow = scrollRow;
    internal int ScrollRow { get; }
}

internal sealed class PartitionTerminalView : ITerminalView, ITerminalBackHandler
{
    private readonly PartitionWindow _owner;
    private RectTransform? _container;

    internal PartitionTerminalView(PartitionWindow owner) => _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public void Build(RectTransform container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _owner.AttachOverview(container, _owner.CustomWindow);
    }

    public void Resume()
    {
    }

    public void Suspend() => _owner.SuspendOverview();

    public IReadOnlyList<DisplayEntry> Selection => Array.Empty<DisplayEntry>();

    public void ClearSelection()
    {
    }

    public void OnReturnFromSubScreen() => _owner.RestoreOverview();

    public object StoreState() => _owner.StoreOverviewState();

    public void RestoreState(object? state)
    {
        if (state is PartitionViewState value) _owner.RestoreOverviewState(value);
    }

    public void SettingChanged(string settingName) => _owner.RestoreOverview();

    public void ShowMessage(string textKey)
    {
    }

    public bool HandleBack() => _owner.HandleOverviewBack();

    public void Dispose()
    {
        _owner.SuspendOverview();
        if (_container != null) CustomUIBuilder.ClearContainer(_container);
        _container = null;
    }
}

internal sealed class PartitionRuleScreen : SubScreen, ITerminalBackHandler
{
    private readonly PartitionWindow _owner;

    internal PartitionRuleScreen(PartitionWindow owner) : base(owner) => _owner = owner;

    /// <inheritdoc />
    public override string ScreenId => "partition.rule";

    protected override bool AddBackButton => false;

    protected override bool BuildContent(CustomUIBuilder builder) => _owner.BuildRuleScreen(builder);

    public override bool HandleBack() => _owner.HandleRuleScreenBack();

    internal override void Dispose()
    {
        if (!_owner.IsHostClosing) _owner.ReturnFromRuleScreen();
        base.Dispose();
    }
}
