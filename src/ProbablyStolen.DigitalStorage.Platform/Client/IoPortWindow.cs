using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Machines.IoPort;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.IoPort;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The IO Port window, which replaces the machine's native window like the storage terminal's does: the rail holds the
/// eject condition and the module page, and the body shows the two native cell banks around the direction arrow and
/// the start, pause and cancel buttons, with the status and progress below.
/// </summary>
internal sealed class IoPortWindow : TerminalWindowHost
{
    internal const string InputPanelRole = "io_input";
    internal const string OutputPanelRole = "io_output";
    private const string HostId = "digital_storage_io_port";
    private readonly IoPortCoordinator _coordinator;
    private readonly MemoryTerminalStore _currentView = new();
    private GameItem? _machine;

    /// <summary>The shown machine view, which sizes the window once it is visible.</summary>
    internal IoPortTerminalView? View { get; set; }

    private IoPortWindow(TerminalRegistry registry, ModText text, ITerminalViewSettings settings, ButtonIcons icons,
        IoPortCoordinator coordinator, Action<string> log)
        : base(registry, text, settings, () => false, () => false, log, null, icons)
    {
        _coordinator = coordinator;
    }

    /// <summary>Creates the window of one machine with its own registry.</summary>
    internal static IoPortWindow Create(ModText text, ITerminalViewSettings settings, ButtonIcons icons,
        IoPortCoordinator coordinator, Action<string> log)
    {
        var registry = IoPortTerminalFeature.CreateRegistry((IConfigManager)settings, log,
            owner => new IoPortTerminalView((IoPortWindow)owner),
            owner => new TerminalModulesScreen((IoPortWindow)owner),
            context => ((IoPortWindow)context.Window).ReadSettings()?.CompletionMode,
            (context, mode) => ((IoPortWindow)context.Window).Change(settings => settings.WithCompletionMode(mode)));
        return new IoPortWindow(registry, text, settings, icons, coordinator, log);
    }

    internal IoPortCoordinator Coordinator => _coordinator;

    internal GameItem Machine => _machine ?? throw new InvalidOperationException("The window is not attached.");

    protected override string HostWindowId => HostId;
    protected override string HostTitleKey => "io_port_name";
    protected override float HostWidth => 360f;
    protected override int HostHeight => 260;
    protected override ICurrentTerminalStore CreateCurrentTerminalStore() => _currentView;
    protected override ITerminalFeatureData CreateFeatureData() => NoTerminalFeatureData.Instance;

    internal new void Attach(GameItem machine)
    {
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        base.Attach(machine);
    }

    internal IoPortSettings? ReadSettings() => _machine == null ? null : _coordinator.ReadDisplay(_machine).Settings;

    internal void Change(Func<IoPortSettings, IoPortSettings> change)
    {
        if (_machine != null && _coordinator.TryChangeSettings(_machine, change)) RefreshButtons();
    }

    internal void Cancel()
    {
        if (_machine != null && _coordinator.Cancel(_machine)) RefreshButtons();
    }

    protected override void OnHostShown() => View?.Fit();

}

/// <summary>The IO Port body: borrowed native banks, the transfer controls, and the status with its progress bar.</summary>
internal sealed class IoPortTerminalView : ITerminalView
{
    private const float BankGap = 10f;
    private const float ControlWidth = 96f;
    // A 16 px icon drawn at exactly twice its size, so the pixel art stays sharp.
    private const float ArrowButtonHeight = 36f;
    private const float DisabledIconAlpha = 0.35f;
    private const float StateButton = 28f;
    private const float BarHeight = 8f;
    private static readonly Color BarBackground = new(41f / 255f, 34f / 255f, 52f / 255f, 1f);
    private static readonly Color BarFill = new(125f / 255f, 169f / 255f, 210f / 255f, 1f);
    private readonly IoPortWindow _owner;
    private readonly MachineryPanelHost _input;
    private readonly MachineryPanelHost _output;
    private RectTransform? _container;
    private RectTransform? _inputHolder;
    private RectTransform? _outputHolder;
    private Image? _arrow;
    private Sprite? _arrowLeft;
    private Sprite? _arrowRight;
    private CustomUIElement? _arrowButton;
    private CustomUIElement? _direction;
    private CustomUIElement? _start;
    private CustomUIElement? _pause;
    private CustomUIElement? _cancel;
    private CustomUIElement? _status;
    private CustomUIElement? _percent;
    private RectTransform? _fill;
    private IoPortPanelState? _shown;

    internal IoPortTerminalView(IoPortWindow owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _input = new MachineryPanelHost(owner.WriteDiagnostic, "Digital Storage IO Port Input Host", "io_input_embed", "io_input_restore");
        _output = new MachineryPanelHost(owner.WriteDiagnostic, "Digital Storage IO Port Output Host", "io_output_embed", "io_output_restore");
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
        var text = _owner.Text;
        var builder = new CustomUIBuilder(CustomUIManager.Instance, _owner.CustomWindow, container);
        builder.BeginColumn(spacing: 8f);
        var body = builder.CurrentContainer;

        builder.BeginRow(BankGap, TextAnchor.MiddleCenter);
        _inputHolder = AddHolder(builder, IoPortWindow.InputPanelRole);
        BuildControls(builder, text);
        _outputHolder = AddHolder(builder, IoPortWindow.OutputPanelRole);
        builder.End();
        LendBanks();

        builder.BeginRow(6f, TextAnchor.MiddleLeft);
        builder.AddLabel(string.Empty, "io_status");
        _status = builder.Last;
        if (_status.text != null) _status.text.overflowMode = TextOverflowModes.Ellipsis;
        _status.root.AddComponent<LayoutElement>().flexibleWidth = 1f;
        builder.AddLabel(string.Empty, "io_percent");
        _percent = builder.Last;
        if (_percent.text != null) _percent.text.alignment = TextAlignmentOptions.MidlineRight;
        builder.End();
        _fill = AddProgressBar(builder.CurrentContainer);
        builder.End();

        Refresh(force: true);
        // On the body, not the container, so clearing the container on dispose also stops the refresh.
        body.gameObject.AddComponent<Ticker>().Initialize(() => Refresh(force: false));
    }

    private void BuildControls(CustomUIBuilder builder, ModText text)
    {
        builder.BeginColumn(6f, TextAnchor.MiddleCenter);
        var column = builder.CurrentContainer.gameObject.AddComponent<LayoutElement>();
        column.minWidth = column.preferredWidth = ControlWidth;

        var icons = _owner.ButtonIcons;
        _arrowLeft = icons.Get(ButtonIcons.ToLeft);
        _arrowRight = icons.Get(ButtonIcons.ToRight);
        _arrowButton = TerminalButtonView.AddIcon(builder, icons, ButtonIcons.ToRight, false,
            Clicked(() => _owner.Change(settings => settings.ReverseDirection())), ControlWidth, ArrowButtonHeight);
        _arrow = Icon(_arrowButton);

        builder.AddLabel(string.Empty, "io_direction");
        _direction = builder.Last;
        if (_direction.text != null) _direction.text.alignment = TextAlignmentOptions.Center;
        TerminalControls.SetSize(_direction, ControlWidth, 18f);

        builder.BeginRow(4f, TextAnchor.MiddleCenter);
        _start = TerminalButtonView.AddIcon(builder, icons, ButtonIcons.IoStart, false,
            Clicked(() => _owner.Change(settings => settings.WithRunning(true))), StateButton);
        TerminalTooltipPresenter.SetButtonText(_start, text.Get("io_start"));
        _pause = TerminalButtonView.AddIcon(builder, icons, ButtonIcons.IoPause, false,
            Clicked(() => _owner.Change(settings => settings.WithRunning(false))), StateButton);
        TerminalTooltipPresenter.SetButtonText(_pause, text.Get("io_pause"));
        _cancel = TerminalButtonView.AddIcon(builder, icons, ButtonIcons.IoCancel, false, Clicked(_owner.Cancel), StateButton);
        TerminalTooltipPresenter.SetButtonText(_cancel, text.Get("io_cancel"));
        builder.End();

        builder.End();
    }

    private static RectTransform AddHolder(CustomUIBuilder builder, string role)
    {
        var holder = new GameObject(role, typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(builder.CurrentContainer, false);
        holder.gameObject.AddComponent<LayoutElement>();
        return holder;
    }

    // The banks are shown only while this body is. A sub-page hands them back to the native window first, so a hidden
    // body never keeps native slots that still render, at a stale place, above the sub-page.
    private void LendBanks()
    {
        Lend(_input, IoPortWindow.InputPanelRole, _inputHolder);
        Lend(_output, IoPortWindow.OutputPanelRole, _outputHolder);
    }

    private void ReturnBanks(string reason)
    {
        _input.Restore(reason);
        _output.Restore(reason);
    }

    private void Lend(MachineryPanelHost host, string role, RectTransform? holder)
    {
        if (holder == null) return;
        if (_owner.Machine.contentWindow?.childElement is not MachineryPanelRow row || row.GetPanel(role) is not { } panel)
        {
            _owner.WriteDiagnostic($"io_port_bank role={role} result=missing");
            return;
        }
        MachineryContainerUtils.TryResize(panel.content, 0, 0);
        panel.ResizePixels(0, 0);
        panel.Validate();
        var scale = 1f / TerminalWindowMetrics.NativeScale;
        var layout = holder.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = panel.widthPixels * scale;
        layout.minHeight = layout.preferredHeight = panel.heightPixels * scale;
        host.Show(panel, row, holder, scale);
    }

    private void Refresh(bool force)
    {
        var state = IoPortPanelState.From(_owner.Coordinator.ReadDisplay(_owner.Machine));
        if (!force && _shown != null && Same(_shown, state)) return;
        _shown = state;
        var text = _owner.Text;
        if (_arrow != null) _arrow.sprite = state.ArrowPointsRight ? _arrowRight : _arrowLeft;
        if (_arrowButton != null)
            TerminalTooltipPresenter.SetButtonText(_arrowButton, state.DirectionTextKey == null ? string.Empty : text.Get(state.DirectionTextKey));
        SetText(_direction, state.DirectionTextKey == null ? string.Empty : text.Get(state.DirectionTextKey));
        // The button for the state the machine is already in is off, so the other one shows what a press does.
        SetEnabled(_arrowButton, state.SettingsKnown);
        SetEnabled(_start, state.SettingsKnown && !state.Running);
        SetEnabled(_pause, state.SettingsKnown && state.Running);
        SetEnabled(_cancel, state.SettingsKnown);
        SetText(_status, text.Get(state.StatusTextKey));
        SetText(_percent, state.Progress is { } value ? text.Get("io_progress", Mathf.FloorToInt((float)value * 100f)) : string.Empty);
        if (_fill != null) _fill.anchorMax = new Vector2(Mathf.Clamp01((float)(state.Progress ?? 0d)), 1f);
    }

    private static bool Same(IoPortPanelState left, IoPortPanelState right) =>
        left.SettingsKnown == right.SettingsKnown && left.Running == right.Running &&
        left.ArrowPointsRight == right.ArrowPointsRight && left.Status == right.Status && left.Progress == right.Progress;

    private static void SetText(CustomUIElement? element, string value)
    {
        if (element?.text != null && element.text.text != value) element.text.text = value;
    }

    private static Image? Icon(CustomUIElement button) =>
        button.root.transform.Find("button_icon")?.GetComponent<Image>();

    private static void SetEnabled(CustomUIElement? button, bool enabled)
    {
        if (button == null) return;
        if (button.root.GetComponent<Selectable>() is { } selectable) selectable.interactable = enabled;
        if (Icon(button) is { } icon) icon.color = new Color(1f, 1f, 1f, enabled ? 1f : DisabledIconAlpha);
    }

    // A pressed button keeps the event system's selection and draws its selected tint as a white frame; release it.
    private static Action Clicked(Action action) => () =>
    {
        action();
        EventSystem.current?.SetSelectedGameObject(null);
    };

    private static RectTransform AddProgressBar(RectTransform parent)
    {
        var track = new GameObject("io_progress", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        track.transform.SetParent(parent, false);
        track.color = BarBackground;
        track.raycastTarget = false;
        var layout = track.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = BarHeight;
        layout.flexibleWidth = 1f;
        var fill = new GameObject("fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(track.transform, false);
        fill.color = BarFill;
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        return fill.rectTransform;
    }

    public void Resume() => Refresh(force: true);

    public void Suspend() => ReturnBanks("io_port_sub_screen");

    public IReadOnlyList<DisplayEntry> Selection => Array.Empty<DisplayEntry>();

    public void ClearSelection()
    {
    }

    public void OnReturnFromSubScreen()
    {
        LendBanks();
        Refresh(force: true);
        Fit();
    }

    public object? StoreState() => null;

    public void RestoreState(object? state)
    {
    }

    public void SettingChanged(string settingName)
    {
    }

    public void ShowMessage(string textKey) => SetText(_status, _owner.Text.Get(textKey));

    public void Dispose()
    {
        if (ReferenceEquals(_owner.View, this)) _owner.View = null;
        _input.Dispose("io_port_view_closed");
        _output.Dispose("io_port_view_closed");
        if (_container != null) CustomUIBuilder.ClearContainer(_container);
        _container = null;
    }

    // Reads the latest machine snapshot a few times a second; the snapshot itself never scans storage.
    private sealed class Ticker : MonoBehaviour
    {
        private const float Interval = 0.15f;
        private Action? _tick;
        private float _next;

        internal void Initialize(Action tick) => _tick = tick;

        private void LateUpdate()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            _tick?.Invoke();
        }
    }
}
