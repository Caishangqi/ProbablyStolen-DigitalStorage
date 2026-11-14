using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;

/// <summary>
/// Draws a terminal's native module panel (title, frame and bay) inside a custom window. Only the panel's render node
/// moves: the machinery row keeps it in its panel list, so save collection (<c>SaveManager.EncodeNode</c> walks the
/// item graph), <c>MachineHelper.GetModuleInv</c> and quick transfer (<c>MachineryPanelRow.CollectStorage</c> and
/// <c>TryFindOneValidInventorySlot</c> read that list, not render children) are unchanged. Native drops find the bay
/// because <c>RenderHandler.RaycastFindParentElement</c> resolves through the hit node's own element chain. The panel
/// centres the bay itself (<c>MachineryPanel.Validate</c>). Nothing native re-parents the panel back (the row does so
/// only when a panel is added), so every exit path must call <see cref="Restore"/>.
/// </summary>
internal sealed class ModuleBayHost : MachineryWidget
{
    private const string ModulesPanelRole = "module";
    private readonly Action<string> _diagnostic;
    private MachineryPanel? _panel;
    private MachineryPanelRow? _home;

    internal ModuleBayHost(Action<string> diagnostic)
    {
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        handler = TreeNodeRender.Instantiate(this, "Digital Storage Terminal Module Bay Host");
        // The node only holds the panel; it must not catch pointer events where the panel leaves room.
        handler.image.enabled = false;
        handler.gameObject.AddComponent<RestoreOnDestroy>().Initialize(this);
    }

    /// <summary>Gets whether the panel is currently drawn here.</summary>
    internal bool IsShowing => _panel != null && ((PixelElement)_panel).IsRenderingChildOf(this);

    public bool fillWidth => false;

    public bool canResize => false;

    public int widthPixels => _panel?.widthPixels ?? 0;

    public int heightPixels => _panel?.heightPixels ?? 0;

    public TreeNodeRender handler { get; }

    public RectTransform rectTransform => handler.rectTransform;

    public TreeNodeRender inner => handler;

    /// <summary>Gets the terminal's module bay, or false.</summary>
    internal static bool TryFind(GameItem terminal, out GameGridInventory bay)
    {
        bay = null!;
        if (!TryFindPanel(terminal, out var panel, out _) || panel.content is not MachineryIntake intake ||
            intake.content is not GameGridInventory grid)
            return false;
        bay = grid;
        return true;
    }

    /// <summary>Gets the terminal's module panel and the machinery row that lays it out, or false.</summary>
    internal static bool TryFindPanel(GameItem terminal, out MachineryPanel panel, out MachineryPanelRow row)
    {
        panel = null!;
        row = null!;
        if (terminal.contentWindow?.childElement is not MachineryPanelRow found ||
            found.GetPanel(ModulesPanelRole) is not { } modules)
            return false;
        panel = modules;
        row = found;
        return true;
    }

    /// <summary>
    /// Moves the host node under <paramref name="container"/> and draws the panel there. Returns false and changes
    /// nothing when the panel is not drawn by <paramref name="home"/>, for example because another view already holds it.
    /// </summary>
    internal bool Show(MachineryPanel panel, MachineryPanelRow home, RectTransform container)
    {
        if (panel == null) throw new ArgumentNullException(nameof(panel));
        if (home == null) throw new ArgumentNullException(nameof(home));
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (IsShowing) return true;
        if (!((PixelElement)panel).IsRenderingChildOf(home))
        {
            _diagnostic("module_bay_embed result=refused reason=not_in_home");
            return false;
        }

        // Unity parent only: the logical TreeNodeRender parent stays tempRoot, so FindParentWindow is null here.
        var node = handler.rectTransform;
        node.SetParent(container, false);
        node.anchorMin = node.anchorMax = node.pivot = new Vector2(0f, 1f);
        node.anchoredPosition = Vector2.zero;
        // Terminal windows draw design units at twice native size and scale down by NativeScale; undo that so the bay
        // keeps its native size relative to the rest of the window.
        var scale = 1f / TerminalWindowMetrics.NativeScale;
        node.localScale = new Vector3(scale, scale, 1f);
        node.SetAsLastSibling();

        _panel = panel;
        _home = home;
        ((PixelElement)panel).SetRenderingParent(this);
        panel.handler.rectTransform.anchoredPosition = Vector2.zero;
        _diagnostic($"module_bay_embed result=shown target=panel size={panel.widthPixels}x{panel.heightPixels} scale={scale:0.##}");
        return true;
    }

    /// <summary>Returns the panel to its machinery row, which lays it out again. Safe to call any number of times.</summary>
    internal void Restore(string reason)
    {
        var panel = _panel;
        var home = _home;
        _panel = null;
        _home = null;
        if (panel == null || home == null) return;
        try
        {
            if (((PixelElement)panel).IsRenderingChildOf(this))
            {
                ((PixelElement)panel).SetRenderingParent(home);
                home.Validate();
            }

            _diagnostic($"module_bay_restore result=ok reason={reason}");
        }
        catch (Exception error)
        {
            _diagnostic($"module_bay_restore result=failed reason={reason} error={error.GetType().Name}");
        }
    }

    /// <summary>Restores the panel and destroys the host node.</summary>
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

    /// <summary>
    /// Last resort for destruction that skips the window's close callback (scene teardown). Whether Unity allows the
    /// re-parent here is unverified; the item graph is rebuilt on load either way, so a failure is only logged.
    /// </summary>
    private sealed class RestoreOnDestroy : MonoBehaviour
    {
        private ModuleBayHost? _host;

        internal void Initialize(ModuleBayHost host) => _host = host;

        private void OnDestroy() => _host?.Restore("destroyed");
    }
}
