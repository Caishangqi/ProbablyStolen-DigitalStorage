using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Bridge between native window focus and the mod's registered back targets.</summary>
public static class WindowBackBridge
{
    private static readonly WindowBackCoordinator Coordinator = new();
    private static readonly List<WindowRegistration> Windows = new();

    internal static IDisposable Register(CustomUIWindow window, RectTransform viewport, Action back)
    {
        var entry = new WindowRegistration(window, viewport, back);
        Windows.Add(entry);
        entry.Focus();
        return entry;
    }

    /// <summary>Handles back only when a registered window is ahead of every native escape target.</summary>
    public static bool Dispatch(StoreUIManager store)
    {
        long? native = null;
        if (WindowsHandler.current != null)
            foreach (var window in WindowsHandler.current.visibleWindows)
                if (window.canMove && (!native.HasValue || window.focusStamp > native.Value)) native = window.focusStamp;
        var paper = PaperStamp(store);
        if (paper.HasValue && (!native.HasValue || paper.Value > native.Value)) native = paper;
        return Coordinator.Dispatch(Time.frameCount, native);
    }

    private static long? PaperStamp(StoreUIManager? store)
    {
        long? stamp = null;
        if (store != null && store.paperUICanvasObject != null)
        {
            var root = store.paperUICanvasObject.transform;
            foreach (Transform panel in root)
                if (panel.gameObject.activeSelf) IncludePanel(panel.gameObject, ref stamp);
            foreach (var panel in UIDragHandler.GetActiveDockedPanels(root)) IncludePanel(panel, ref stamp);
        }
        return stamp;
    }

    private static void IncludePanel(GameObject panel, ref long? native)
    {
        var stamp = PaperPanelFocusStamp.GetStamp(panel);
        if (!native.HasValue || stamp > native.Value) native = stamp;
    }

    /// <summary>Restores mod viewports below the paper layer when the native paper family gains focus.</summary>
    public static void PanelsRaised()
    {
        var root = RenderHandler.current?.windowRoot?.rectTransform;
        if (root == null) return;
        foreach (var entry in Windows.ToArray())
            if (entry.Viewport != null) entry.Viewport.SetParent(root, false);
    }

    /// <summary>Places mod viewports among native windows according to their shared focus stamps.</summary>
    public static void WindowsRaised(PixelWindow? focused)
    {
        var root = UIFamilyLayer.RaisedCanvas == null ? null :
            UIFamilyLayer.RaisedCanvas.transform.Find("FloatingWindowRoot");
        if (root == null) return;
        var ordered = new List<(Transform Transform, long Stamp)>();
        var paper = PaperStamp(StoreUIManager.Instance);
        var home = RenderHandler.current?.windowRoot?.rectTransform;
        if (WindowsHandler.current != null)
            foreach (var window in WindowsHandler.current.visibleWindows)
                if (window.canMove && window.rectTransform != null && window.rectTransform.parent == root)
                    ordered.Add((window.rectTransform, window.focusStamp));
        if (focused != null && focused.canMove && focused.rectTransform != null && focused.rectTransform.parent == root &&
            !ordered.Any(item => item.Transform == focused.rectTransform))
            ordered.Add((focused.rectTransform, focused.focusStamp));
        foreach (var entry in Windows.ToArray())
        {
            if (entry.Viewport == null || !entry.Active()) continue;
            if (!WindowBackCoordinator.AbovePaper(entry.Stamp, paper))
            {
                if (home != null) entry.Viewport.SetParent(home, false);
                continue;
            }
            entry.Viewport.SetParent(root, false);
            ordered.Add((entry.Viewport, entry.Stamp));
        }
        foreach (var item in ordered.OrderBy(item => item.Stamp)) item.Transform.SetAsLastSibling();
    }

    internal sealed class WindowRegistration : IDisposable
    {
        private readonly CustomUIWindow _window;
        private readonly WindowBackCoordinator.Registration _registration;
        internal readonly RectTransform Viewport;
        internal long Stamp;

        internal WindowRegistration(CustomUIWindow window, RectTransform viewport, Action back)
        {
            _window = window;
            Viewport = viewport;
            _registration = Coordinator.Register(Active, back);
        }

        internal bool Active() => _window.IsAlive && _window.IsVisible && Viewport != null && Viewport.gameObject.activeInHierarchy;

        internal void Focus()
        {
            Stamp = PixelWindow.NextFocusStamp();
            _registration.Focus(Stamp);
            UIFamilyLayer.BringWindowsToFront(null);
            WindowsRaised(null);
        }

        public void Dispose()
        {
            _registration.Dispose();
            Windows.Remove(this);
        }
    }
}
