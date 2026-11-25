using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Drive;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The drive window, which replaces the machine's native window like the storage terminal's does: the left dock holds
/// the module and priority pages, and the body shows the native cell bays.
/// </summary>
internal sealed class DriveWindow : TerminalWindowHost
{
    private const string HostId = "digital_storage_drive";
    private readonly MemoryTerminalStore _currentView = new();

    private DriveWindow(TerminalRegistry registry, ModText text, ITerminalViewSettings settings, ButtonIcons icons,
        Action<string> log) : base(registry, text, settings, () => false, () => false, log, null, icons)
    {
    }

    /// <summary>The shown cell bay view, which sizes the window once it is visible.</summary>
    internal DriveTerminalView? View { get; set; }

    /// <summary>Creates the window of one drive with its own registry.</summary>
    internal static DriveWindow Create(ModText text, ITerminalViewSettings settings, ButtonIcons icons, Action<string> log)
    {
        var registry = DriveTerminalFeature.CreateRegistry((IConfigManager)settings, log,
            owner => new DriveTerminalView((DriveWindow)owner),
            owner => new TerminalModulesScreen((DriveWindow)owner),
            owner => new DrivePriorityScreen((DriveWindow)owner));
        return new DriveWindow(registry, text, settings, icons, log);
    }

    protected override string HostWindowId => HostId;
    protected override string HostTitleKey => "drive_name";
    protected override float HostWidth => 360f;
    protected override int HostHeight => 200;
    protected override ICurrentTerminalStore CreateCurrentTerminalStore() => _currentView;
    protected override ITerminalFeatureData CreateFeatureData() => NoTerminalFeatureData.Instance;

    internal static bool HasLiveOwner(GameItem drive) => drive is GameItemElement element && !element.IsDestroyed() &&
        drive.contentWindow is { } window && window.parentItems.Contains(drive);

    internal override void Open()
    {
        if (HasLiveOwner(Terminal)) base.Open();
    }

    protected override bool SessionIsCurrent() => base.SessionIsCurrent() && HasLiveOwner(Terminal);

    protected override void OnHostShown()
    {
        View?.Fit();
        CustomWindow.root.AddComponent<TerminalInventoryRefreshHandler>().Initialize(() => IsCurrentSession());
    }
}

/// <summary>The drive body: the borrowed native cell bays.</summary>
internal sealed class DriveTerminalView : ITerminalView
{
    private readonly DriveWindow _owner;
    private readonly MachineryPanelHost _cells;
    private RectTransform? _container;
    private RectTransform? _holder;

    internal DriveTerminalView(DriveWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _cells = new MachineryPanelHost(owner.WriteDiagnostic, "Digital Storage Drive Cells Host", "drive_cells_embed",
            "drive_cells_restore");
        owner.View = this;
    }

    /// <summary>Sizes the window to the body; called once the window is visible.</summary>
    internal void Fit()
    {
        if (_container != null) _owner.FitBodyTo(_container);
    }

    public void Build(RectTransform container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _owner.CustomWindow, container);
        builder.BeginColumn(spacing: 0f, TextAnchor.MiddleCenter);
        _holder = new GameObject(MachineContentWindow.CellsPanelRole, typeof(RectTransform)).GetComponent<RectTransform>();
        _holder.SetParent(builder.CurrentContainer, false);
        _holder.gameObject.AddComponent<LayoutElement>();
        builder.End();
        LendCells();
    }

    // The bays are shown only while this body is. A sub-page hands them back to the native window first, so a hidden
    // body never keeps native slots that still render, at a stale place, above the sub-page.
    private void LendCells()
    {
        if (_holder == null) return;
        if (_owner.Terminal.contentWindow?.childElement is not MachineryPanelRow row ||
            row.GetPanel(MachineContentWindow.CellsPanelRole) is not { } panel)
        {
            _owner.WriteDiagnostic("drive_cells result=missing");
            return;
        }

        MachineryContainerUtils.TryResize(panel.content, 0, 0);
        panel.ResizePixels(0, 0);
        panel.Validate();
        var scale = 1f / TerminalWindowMetrics.NativeScale;
        var layout = _holder.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = panel.widthPixels * scale;
        layout.minHeight = layout.preferredHeight = panel.heightPixels * scale;
        _cells.Show(panel, row, _holder, scale);
    }

    public void Resume()
    {
    }

    public void Suspend() => _cells.Restore("drive_sub_screen");

    public IReadOnlyList<DisplayEntry> Selection => Array.Empty<DisplayEntry>();

    public void ClearSelection()
    {
    }

    public void OnReturnFromSubScreen()
    {
        LendCells();
        Fit();
    }

    public object? StoreState() => null;

    public void RestoreState(object? state)
    {
    }

    public void SettingChanged(string settingName)
    {
    }

    public void ShowMessage(string textKey)
    {
    }

    public void Dispose()
    {
        if (ReferenceEquals(_owner.View, this)) _owner.View = null;
        _cells.Dispose("drive_view_closed");
        if (_container != null) CustomUIBuilder.ClearContainer(_container);
        _container = null;
    }
}
