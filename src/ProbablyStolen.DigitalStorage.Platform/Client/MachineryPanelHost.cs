using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Draws one native machinery panel (title, frame and contents) inside a mod window. Only the panel's render node
/// moves: the machinery row keeps it in its panel list, so save collection (<c>SaveManager.EncodeNode</c> walks the
/// item graph), <c>MachineHelper.GetModuleInv</c> and quick transfer (<c>MachineryPanelRow.CollectStorage</c> and
/// <c>TryFindOneValidInventorySlot</c> read that list, not render children) are unchanged. Native drops find the
/// panel's inventories because <c>RenderHandler.RaycastFindParentElement</c> resolves through the hit node's own
/// element chain. Nothing native re-parents the panel back (the row does so only when a panel is added), so every
/// exit path must call <see cref="Restore"/>.
/// </summary>
internal sealed class MachineryPanelHost : MachineryWidget
{
    private static readonly HashSet<MachineryPanelHost> Showing = new();
    private readonly Action<string> _diagnostic;
    private readonly string _showEvent;
    private readonly string _restoreEvent;
    private MachineryPanel? _panel;
    private MachineryPanelRow? _home;
    private int _originalSiblingIndex = -1;

    internal MachineryPanelHost(Action<string> diagnostic, string hostName, string showEvent, string restoreEvent)
    {
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        _showEvent = showEvent ?? throw new ArgumentNullException(nameof(showEvent));
        _restoreEvent = restoreEvent ?? throw new ArgumentNullException(nameof(restoreEvent));
        handler = TreeNodeRender.Instantiate(this, hostName ?? throw new ArgumentNullException(nameof(hostName)));
        handler.image.enabled = false;
        handler.gameObject.AddComponent<RestoreOnDestroy>().Initialize(this);
    }

    internal bool IsShowing => _panel != null && ((PixelElement)_panel).IsRenderingChildOf(this);

    /// <summary>Gets the native panels some host is showing right now.</summary>
    internal static IReadOnlyList<(MachineryPanel Panel, NativeWindowSizer Window)> ShownPanels
    {
        get
        {
            var panels = new List<(MachineryPanel, NativeWindowSizer)>();
            foreach (var host in Showing)
            {
                if (host.handler == null || !host.handler.gameObject.activeInHierarchy || !host.IsShowing) continue;
                var window = host.handler.GetComponentInParent<NativeWindowSizer>();
                if (window != null) panels.Add((host._panel!, window));
            }

            return panels;
        }
    }

    public bool fillWidth => false;
    public bool canResize => false;
    public int widthPixels => _panel?.widthPixels ?? 0;
    public int heightPixels => _panel?.heightPixels ?? 0;
    public TreeNodeRender handler { get; }
    public RectTransform rectTransform => handler.rectTransform;
    public TreeNodeRender inner => handler;

    /// <summary>Moves only the render node of a panel still owned by its original native row.</summary>
    internal bool Show(MachineryPanel panel, MachineryPanelRow home, RectTransform container, float localScale)
    {
        if (panel == null) throw new ArgumentNullException(nameof(panel));
        if (home == null) throw new ArgumentNullException(nameof(home));
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (!(localScale > 0f) || float.IsInfinity(localScale)) throw new ArgumentOutOfRangeException(nameof(localScale));
        if (IsShowing && ReferenceEquals(_panel, panel) && ReferenceEquals(_home, home) && IsOwnedBy(panel, home))
            return true;
        if (_panel != null) Restore("stale_host");
        if (!IsOwnedBy(panel, home))
        {
            _diagnostic($"{_showEvent} result=refused reason=not_owned_here");
            return false;
        }
        if (!((PixelElement)panel).IsRenderingChildOf(home))
        {
            _diagnostic($"{_showEvent} result=refused reason=not_in_home");
            return false;
        }

        var node = handler.rectTransform;
        node.SetParent(container, false);
        node.anchorMin = node.anchorMax = node.pivot = new Vector2(0f, 1f);
        node.anchoredPosition = Vector2.zero;
        node.localScale = new Vector3(localScale, localScale, 1f);
        node.SetAsLastSibling();

        _originalSiblingIndex = panel.handler.rectTransform.GetSiblingIndex();
        _panel = panel;
        _home = home;
        ((PixelElement)panel).SetRenderingParent(this);
        panel.handler.rectTransform.anchoredPosition = Vector2.zero;
        Showing.Add(this);
        _diagnostic($"{_showEvent} result=shown target=panel size={panel.widthPixels}x{panel.heightPixels} scale={localScale:0.##}");
        return true;
    }

    /// <summary>Returns an attached panel to its native row, or releases a panel detached while borrowed.</summary>
    internal void Restore(string reason)
    {
        var panel = _panel;
        var home = _home;
        var originalSiblingIndex = _originalSiblingIndex;
        _panel = null;
        _home = null;
        _originalSiblingIndex = -1;
        Showing.Remove(this);
        if (panel == null || home == null) return;
        try
        {
            if (((PixelElement)panel).IsRenderingChildOf(this))
            {
                if (IsOwnedBy(panel, home))
                {
                    ((PixelElement)panel).SetRenderingParent(home);
                    home.Validate();
                    var parent = panel.handler.rectTransform.parent;
                    if (originalSiblingIndex >= 0 && parent != null && parent.childCount > 0)
                        panel.handler.rectTransform.SetSiblingIndex(Math.Min(originalSiblingIndex, parent.childCount - 1));
                }
                else
                {
                    ((PixelElement)panel).RemoveRenderingParent(this);
                }
            }
            _diagnostic($"{_restoreEvent} result=ok reason={reason}");
        }
        catch (Exception error)
        {
            _diagnostic($"{_restoreEvent} result=failed reason={reason} error={error.GetType().Name}");
        }
    }

    private static bool IsOwnedBy(MachineryPanel panel, MachineryPanelRow row)
    {
        if (!ReferenceEquals(panel.owner, row)) return false;
        var panels = row.Panels;
        for (var index = 0; index < panels.Count; index++)
            if (ReferenceEquals(panels[index], panel)) return true;
        return false;
    }

    internal void Dispose(string reason)
    {
        Restore(reason);
        if (handler != null && handler.gameObject != null) UnityEngine.Object.Destroy(handler.gameObject);
    }

    public void Validate()
    {
        if (IsShowing) _panel!.handler.rectTransform.anchoredPosition = Vector2.zero;
    }

    public void ResizePixels(int width, int height) => Validate();
    public int WidthPixelsClamp(int width) => widthPixels;
    public int HeightPixelsClamp(int height) => heightPixels;
    public int XClamp(int x) => 0;
    public int YClamp(int y) => 0;
    public bool Attach(PixelElement element) => false;
    public bool Detach(PixelElement element) => false;

    /// <summary>Gets whether <paramref name="target"/> is part of a native panel some host is showing right now.</summary>
    internal static bool IsInsideShownPanel(Transform target) =>
        target != null && target.GetComponentInParent<RestoreOnDestroy>() is { Host: { IsShowing: true } };

    private sealed class RestoreOnDestroy : MonoBehaviour
    {
        private MachineryPanelHost? _host;
        internal MachineryPanelHost? Host => _host;
        internal void Initialize(MachineryPanelHost host) => _host = host;
        private void OnDestroy() => _host?.Restore("destroyed");
    }
}
