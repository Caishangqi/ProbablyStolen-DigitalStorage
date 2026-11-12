using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Draws a cell's module bay in its workbench window. Only the render node moves; storage ownership stays with the
/// cell's hidden module intake. Native save collection follows panel content, while drops resolve the bay through
/// <c>TreeNodeRender.FindParentElement</c> at the rendered position.
/// </summary>
internal sealed class ModuleBayView : MachineryWidget
{
    private GameGridInventory? _bay;
    private PixelElement? _home;

    internal ModuleBayView()
    {
        handler = TreeNodeRender.Instantiate(this, "Digital Storage Module Bay View");
        // The node only holds the bay; it must not catch pointer events where the bay leaves room.
        handler.image.enabled = false;
    }

    public bool fillWidth => false;

    public bool canResize => false;

    // The view is as large as the bay, so expanding the bay grows the workbench panel too.
    public int widthPixels => _bay?.widthPixels ?? 0;

    public int heightPixels => _bay?.heightPixels ?? 0;

    public TreeNodeRender handler { get; }

    public RectTransform rectTransform => handler.rectTransform;

    public TreeNodeRender inner => handler;

    /// <summary>
    /// Draws a cell's bay here; <paramref name="home"/> is the element it is drawn in otherwise, the intake of the
    /// cell's modules panel, and it returns there.
    /// </summary>
    internal void Show(GameGridInventory bay, PixelElement home)
    {
        if (ReferenceEquals(_bay, bay) && ((PixelElement)bay).IsRenderingChildOf(this)) return;
        Clear();
        _bay = bay;
        _home = home;
        ((PixelElement)bay).SetRenderingParent(this);
        Validate();
        Relayout();
    }

    /// <summary>Returns the shown bay to its cell's modules panel.</summary>
    internal void Clear()
    {
        var bay = _bay;
        var home = _home;
        _bay = null;
        _home = null;
        if (bay == null) return;
        if (home != null && ((PixelElement)bay).IsRenderingChildOf(this))
        {
            ((PixelElement)bay).SetRenderingParent(home);
            home.Validate();
        }

        Relayout();
    }

    /// <summary>
    /// Lays the workbench row out for the new bay size. The row stretches every panel to the tallest one and each
    /// panel keeps that as its requested size, so a panel would stay large after the cell leaves; resetting the
    /// requested sizes first lets the window shrink back (the same reset as the cell window's hidden pages).
    /// </summary>
    private void Relayout()
    {
        if (handler.parent?.FindParentElement<MachineryPanelRow>() is not { } row) return;
        foreach (var panel in row.Panels)
        {
            MachineryContainerUtils.TryResize(panel.content, 0, 0);
            panel.ResizePixels(0, 0);
        }

        row.Validate();
    }

    public void Validate()
    {
        // The hidden home panel centres its content when it validates; drawn here, the bay sits at the origin.
        if (_bay != null && ((PixelElement)_bay).IsRenderingChildOf(this))
            _bay.handler.rectTransform.anchoredPosition = Vector2.zero;
    }

    public void ResizePixels(int width, int height) => Validate();

    public int WidthPixelsClamp(int width) => widthPixels;

    public int HeightPixelsClamp(int height) => heightPixels;

    public int XClamp(int x) => 0;

    public int YClamp(int y) => 0;

    public bool Attach(PixelElement element) => false;

    public bool Detach(PixelElement element) => false;
}
