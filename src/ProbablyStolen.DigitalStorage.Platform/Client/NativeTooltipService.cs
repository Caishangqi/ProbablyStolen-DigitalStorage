using System;
using System.Collections.Generic;
using System.Threading;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ClientLayout = ProbablyStolen.DigitalStorage.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Game adapter for the public tooltip contracts. All UI calls are made on the game UI thread.</summary>
internal sealed class NativeTooltipService : ITooltipService, IDisposable
{
    private readonly List<TooltipGroup> _groups = new();
    private readonly Dictionary<string, ObstacleRegistration> _obstacles = new(StringComparer.Ordinal);
    private readonly Dictionary<CustomUIElement, Binding> _bindings = new();
    private readonly Func<Sprite?> _rgbFrame;
    private readonly Action<string> _log;
    private readonly int _mainThreadId;
    private readonly List<Binding> _bindingSweep = new();
    private readonly List<TooltipGroup> _tickGroups = new();
    private readonly List<ObstacleRegistration> _obstacleSnapshot = new();
    private TooltipServiceDriver? _driver;
    private NativeButtonTooltip? _buttonTooltip;
    private int _nextOwnerSweepFrame;
    private int _visibleGroupCount;
    private bool _ticking;
    private bool _disposed;
    private int _lastConsumedFrame = -1;

    internal NativeTooltipService(Func<Sprite?> rgbFrame, Action<string> log)
    {
        _rgbFrame = rgbFrame ?? throw new ArgumentNullException(nameof(rgbFrame));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    public ITooltip CreateTooltip(object owner) => CreateGroup(owner, preferPopupAnchor: false);

    internal ITooltip CreatePopupTooltip(RectTransform popup) => CreateGroup(popup.gameObject, preferPopupAnchor: true);

    private TooltipGroup CreateGroup(object owner, bool preferPopupAnchor)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        ThrowIfDisposed();
        ThrowIfWrongThread();
        var group = new TooltipGroup(this, owner, preferPopupAnchor);
        _groups.Add(group);
        EnsureDriver();
        return group;
    }

    public IDisposable SetTooltip(CustomUIElement element, string text, object owner)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        ThrowIfDisposed();
        ThrowIfWrongThread();
        return BindTooltip(element, text, owner, button: false);
    }

    internal void SetButtonTooltip(CustomUIElement element, string text)
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (string.IsNullOrEmpty(text))
        {
            ClearTooltip(element);
            return;
        }
        if (_bindings.TryGetValue(element, out var binding) && binding.Trigger is { IsButton: true } trigger && trigger != null)
        {
            trigger.tooltipText = text;
            return;
        }
        BindTooltip(element, text, element, button: true);
    }

    internal bool TryShowButtonTooltip(DigitalStorageTooltipTrigger trigger)
    {
        if (_disposed) return false;
        EnsureDriver();
        return _driver != null && _driver.isActiveAndEnabled && (_buttonTooltip ??= new NativeButtonTooltip()).TryShow(trigger);
    }

    internal void HideButtonTooltip(DigitalStorageTooltipTrigger trigger) => _buttonTooltip?.Hide(trigger);

    private IDisposable BindTooltip(CustomUIElement element, string text, object owner, bool button)
    {
        if (_bindings.TryGetValue(element, out var previous)) previous.Dispose();
        if (element.root == null || string.IsNullOrEmpty(text)) return new ActionDisposable(() => { });
        var trigger = element.root.AddComponent<DigitalStorageTooltipTrigger>();
        trigger.tooltipText = text;
        if (button) trigger.UseButtonTooltip(this);
        var binding = new Binding(this, element, trigger, owner);
        _bindings[element] = binding;
        EnsureDriver();
        return binding;
    }

    internal void ClearTooltip(CustomUIElement element)
    {
        ThrowIfWrongThread();
        if (_bindings.TryGetValue(element, out var binding)) binding.Dispose();
        if (element?.root == null) return;
        foreach (var trigger in element.root.GetComponents<DigitalStorageTooltipTrigger>())
            UnityEngine.Object.Destroy(trigger);
    }

    public IDisposable RegisterObstacle(string id, Func<IReadOnlyList<RectTransform>> provider)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An obstacle id is required.", nameof(id));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        ThrowIfDisposed();
        ThrowIfWrongThread();
        var registration = new ObstacleRegistration(this, id, provider);
        if (_obstacles.TryGetValue(id, out var old)) old.Dispose();
        _obstacles[id] = registration;
        return registration;
    }

    public bool TryConsumeScroll(bool altHeld, float wheelDelta, int frameId) =>
        TryConsumeScroll(altHeld, wheelDelta, frameId, null);

    internal bool TryConsumeScroll(bool altHeld, float wheelDelta, int frameId, ITooltip? target)
    {
        ThrowIfWrongThread();
        if (_disposed || !altHeld || wheelDelta == 0f || _lastConsumedFrame == frameId || _visibleGroupCount == 0) return false;
        for (var index = _groups.Count - 1; index >= 0; index--)
        {
            var group = _groups[index];
            if (!group.IsVisible || (target != null && !ReferenceEquals(target, group))) continue;
            if (!IsOwnerAlive(group.Owner)) { group.Dispose(); continue; }
            group.PrepareForScroll(frameId);
            if (!group.IsVisible || !group.IsOverflowing) continue;
            group.Scroll(wheelDelta);
            _lastConsumedFrame = frameId;
            return true;
        }
        return false;
    }

    private void CleanupStaleOwners(int frameId)
    {
        if (frameId < _nextOwnerSweepFrame) return;
        _nextOwnerSweepFrame = frameId + 60;
        _bindingSweep.Clear();
        foreach (var binding in _bindings.Values) _bindingSweep.Add(binding);
        foreach (var binding in _bindingSweep)
            if (!IsOwnerAlive(binding.Owner)) binding.Dispose();
        _bindingSweep.Clear();
        for (var index = _groups.Count - 1; index >= 0; index--)
            if (!IsOwnerAlive(_groups[index].Owner)) _groups[index].Dispose();
    }

    internal void Tick(TooltipServiceDriver driver)
    {
        if (_disposed || _ticking || !ReferenceEquals(_driver, driver)) return;
        _ticking = true;
        try
        {
            CleanupStaleOwners(Time.frameCount);
            _buttonTooltip?.Tick();
            if (_visibleGroupCount == 0) return;
            _tickGroups.Clear();
            foreach (var group in _groups)
                if (group.IsVisible) _tickGroups.Add(group);
            foreach (var group in _tickGroups) group.Tick();
        }
        finally { _tickGroups.Clear(); _ticking = false; }
    }

    internal void EnsureDriver()
    {
        if (_disposed || _driver != null) return;
        // Overlay roots can disappear between ticks. The service's sole driver must still sweep owners and find the next root.
        var node = new GameObject("Digital Storage Tooltip Driver");
        UnityEngine.Object.DontDestroyOnLoad(node);
        _driver = node.AddComponent<TooltipServiceDriver>();
        _driver.Initialize(this);
    }

    private void ReleaseDriver()
    {
        if (_driver != null)
        {
            _driver.Detach();
            UnityEngine.Object.Destroy(_driver.gameObject);
        }
        _driver = null;
    }

    internal void DriverDisabled(TooltipServiceDriver driver)
    {
        if (ReferenceEquals(_driver, driver)) _buttonTooltip?.Hide();
    }

    internal void SceneUnloaded(TooltipServiceDriver driver)
    {
        if (ReferenceEquals(_driver, driver)) _buttonTooltip?.Release();
    }

    internal void DriverDestroyed(TooltipServiceDriver driver)
    {
        if (!ReferenceEquals(_driver, driver)) return;
        _buttonTooltip?.Release();
        _driver = null;
    }

    internal void Remove(TooltipGroup group) => _groups.Remove(group);

    internal void FillObstacles(TooltipGroup group, RectTransform parent, double parentLeft, double parentTop,
        List<ClientLayout.TooltipRect> result)
    {
        result.Clear();
        _obstacleSnapshot.Clear();
        foreach (var registration in _obstacles.Values) _obstacleSnapshot.Add(registration);
        foreach (var registration in _obstacleSnapshot)
        {
            if (_disposed || registration.IsDisposed) continue;
            try
            {
                var transforms = registration.Provider();
                if (transforms == null) continue;
                for (var index = 0; index < transforms.Count; index++)
                {
                    var transform = transforms[index];
                    if (transform != null) result.Add(group.LayoutRect(parent, transform, parentLeft, parentTop));
                }
            }
            catch (Exception error)
            {
                _log($"Digital Storage: tooltip obstacle {registration.Id} failed ({error.GetType().Name}).");
            }
        }
        _obstacleSnapshot.Clear();
    }

    internal Sprite? RgbFrame => _rgbFrame();

    public void Dispose()
    {
        ThrowIfWrongThread();
        if (_disposed) return;
        _disposed = true;
        _buttonTooltip?.Release();
        _buttonTooltip = null;
        ReleaseDriver();
        while (_bindings.Count > 0)
        {
            using var iterator = _bindings.Values.GetEnumerator();
            iterator.MoveNext();
            iterator.Current.Dispose();
        }
        while (_groups.Count > 0) _groups[_groups.Count - 1].Dispose();
        _obstacles.Clear();
    }

    private void RemoveObstacle(ObstacleRegistration registration)
    {
        ThrowIfWrongThread();
        if (_obstacles.TryGetValue(registration.Id, out var current) && ReferenceEquals(current, registration))
            _obstacles.Remove(registration.Id);
    }

    private void RemoveBinding(Binding binding)
    {
        ThrowIfWrongThread();
        if (_bindings.TryGetValue(binding.Element, out var current) && ReferenceEquals(current, binding))
            _bindings.Remove(binding.Element);
        if (binding.Trigger != null)
        {
            if (binding.Trigger.IsButton) binding.Trigger.enabled = false;
            binding.Trigger.tooltipText = string.Empty;
            UnityEngine.Object.Destroy(binding.Trigger);
        }
    }

    private static bool IsOwnerAlive(object owner)
    {
        if (owner is CustomUIElement element) return element.root != null && element.root.activeInHierarchy;
        if (owner is Behaviour behaviour) return behaviour != null && behaviour.isActiveAndEnabled;
        if (owner is GameObject gameObject) return gameObject != null && gameObject.activeInHierarchy;
        if (owner is UnityEngine.Object unityObject) return unityObject != null;
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeTooltipService));
    }

    private void ThrowIfWrongThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            throw new InvalidOperationException("Tooltip UI access is main thread only.");
    }

    private sealed class ActionDisposable : IDisposable
    {
        private Action? _dispose;
        internal ActionDisposable(Action dispose) => _dispose = dispose;
        public void Dispose()
        {
            var dispose = _dispose;
            _dispose = null;
            dispose?.Invoke();
        }
    }

    private sealed class ObstacleRegistration : IDisposable
    {
        private readonly NativeTooltipService _service;
        private bool _disposed;
        internal ObstacleRegistration(NativeTooltipService service, string id, Func<IReadOnlyList<RectTransform>> provider)
        { _service = service; Id = id; Provider = provider; }
        internal string Id { get; }
        internal bool IsDisposed => _disposed;
        internal Func<IReadOnlyList<RectTransform>> Provider { get; }
        public void Dispose() { _service.ThrowIfWrongThread(); if (_disposed) return; _disposed = true; _service.RemoveObstacle(this); }
    }

    private sealed class Binding : IDisposable
    {
        private readonly NativeTooltipService _service;
        private bool _disposed;
        internal Binding(NativeTooltipService service, CustomUIElement element, DigitalStorageTooltipTrigger? trigger, object owner)
        { _service = service; Element = element; Trigger = trigger; Owner = owner; }
        internal CustomUIElement Element { get; }
        internal DigitalStorageTooltipTrigger? Trigger { get; }
        internal object Owner { get; }
        public void Dispose() { _service.ThrowIfWrongThread(); if (_disposed) return; _disposed = true; _service.RemoveBinding(this); }
    }

    internal sealed class TooltipGroup : ITooltip
    {
        private readonly NativeTooltipService _service;
        private readonly object _owner;
        private readonly bool _preferPopupAnchor;
        private readonly Dictionary<string, TooltipPanel> _panels = new(StringComparer.Ordinal);
        private readonly ClientLayout.TooltipScrollState _scroll = new();
        private readonly List<TooltipPanel> _activePanels = new();
        private readonly List<ClientLayout.TooltipPanelMeasure> _measures = new();
        private readonly List<ClientLayout.TooltipRect> _obstacles = new();
        private readonly Vector3[] _corners = new Vector3[4];
        private bool _membershipDirty = true;
        private RectTransform? _anchor;
        private Vector2? _screenPosition;
        private ClientLayout.TooltipDirection? _direction;
        private RectTransform? _root;
        private RectTransform? _viewport;
        private RectTransform? _content;
        private Image? _scrollIndicator;
        private TextMeshProUGUI? _scrollHint;
        private bool? _scrollVisible;
        private bool _disposed;
        private bool _visible;
        private string _source = string.Empty;
        private readonly ClientLayout.TooltipLayoutFrameGate _layoutFrame = new();

        internal TooltipGroup(NativeTooltipService service, object owner, bool preferPopupAnchor)
        { _service = service; _owner = owner; _preferPopupAnchor = preferPopupAnchor; }
        internal object Owner => _owner;
        internal bool IsVisible => _visible;
        internal bool IsOverflowing => _layoutFrame.IsValid && _scroll.IsOverflowing;
        internal NativeTooltipService Service => _service;

        internal void InvalidateLayout()
        {
            _layoutFrame.Invalidate();
        }

        internal void Tick()
        {
            if (_disposed || !_visible) return;
            if (!IsOwnerAlive(_owner)) { Dispose(); return; }
            if (_layoutFrame.TryBegin(Time.frameCount)) Layout();
        }

        internal void PrepareForScroll(int frameId)
        {
            if (_disposed || !_visible || !_layoutFrame.TryBegin(frameId)) return;
            Layout();
        }

        internal void SetSource(string source)
        {
            source ??= string.Empty;
            if (string.Equals(_source, source, StringComparison.Ordinal)) return;
            _source = source;
            _scroll.Reset(source);
            InvalidateLayout();
        }

        internal void HidePanelsExcept(IEnumerable<string> ids)
        {
            _service.ThrowIfWrongThread();
            foreach (var panel in _panels.Values) panel.Active = false;
            foreach (var id in ids)
                if (_panels.TryGetValue(id, out var panel)) panel.Active = true;
            foreach (var panel in _panels.Values)
                if (!panel.Active) panel.Hide();
            _membershipDirty = true;
            InvalidateLayout();
        }

        public ITooltipPanel AddPanel(string id, int order)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A panel id is required.", nameof(id));
            ThrowIfDisposed();
            if (!_panels.TryGetValue(id, out var panel))
            {
                _panels.Add(id, panel = new TooltipPanel(this, id, order));
                _membershipDirty = true;
                InvalidateLayout();
            }
            return panel;
        }

        public void Show(Vector2 screenPosition) { ThrowIfDisposed(); _screenPosition = screenPosition; _anchor = null; InvalidateLayout(); ShowCore(); }
        public void Show(RectTransform anchor) { ThrowIfDisposed(); _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor)); _screenPosition = null; InvalidateLayout(); ShowCore(); }
        public void Hide()
        {
            _service.ThrowIfWrongThread();
            if (_disposed || !_visible) return;
            _visible = false;
            _service._visibleGroupCount--;
            InvalidateLayout();
            foreach (var panel in _panels.Values) panel.Hide();
            SetScrollVisible(false);
        }

        public void Dispose()
        {
            _service.ThrowIfWrongThread();
            if (_disposed) return;
            Hide();
            _disposed = true;
            foreach (var panel in _panels.Values) panel.Dispose();
            _panels.Clear();
            _activePanels.Clear();
            DestroyGroupRoot();
            _service.Remove(this);
        }

        internal void Scroll(float delta)
        {
            _scroll.ScrollNotches(Math.Sign(delta));
            if (_content != null) _content.anchoredPosition = new Vector2(0, (float)_scroll.Offset);
        }

        private void ShowCore()
        {
            ThrowIfDisposed();
            if (_panels.Count == 0) { Hide(); return; }
            if (!_visible) _service._visibleGroupCount++;
            _visible = true;
            _service.EnsureDriver();
        }

        private void Layout()
        {
            if (!_visible || _panels.Count == 0) return;
            var parent = ResolveParent();
            if (parent == null)
            {
                // Keep the requested anchor while a scene's overlay is temporarily unavailable.
                if (!ReferenceEquals(_root, null))
                {
                    foreach (var panel in _panels.Values) panel.DestroyWindow();
                    DestroyGroupRoot();
                }
                return;
            }
            EnsureGroupRoot(parent);
            if (_content == null || _viewport == null) return;

            var canvas = parent.GetComponentInParent<Canvas>();
            var frame = CanvasFrame(parent, canvas);
            if (frame.Width < 1 || frame.Height < 1) { Hide(); return; }
            var parentLeft = frame.X;
            var parentTop = frame.Bottom;
            var native = OverlayHandler.current?.GetTooltipWindow();
            var nativeRect = native?.handler != null && native.IsVisible()
                ? NativeWindowRect(native, parent, parentLeft, parentTop)
                : (ClientLayout.TooltipRect?)null;
            var placementNative = _preferPopupAnchor ? null : nativeRect;
            if (!placementNative.HasValue && _anchor == null && !_screenPosition.HasValue) { Hide(); return; }
            var anchor = placementNative
                ?? (_anchor == null
                    ? ScreenAnchor(parent, canvas, _screenPosition!.Value, parentLeft, parentTop)
                    : LayoutRect(parent, _anchor, parentLeft, parentTop));
            var viewport = ClientLayout.TooltipCoordinateMapper.ToTopLeft(frame, parentLeft, parentTop);
            _service.FillObstacles(this, parent, parentLeft, parentTop, _obstacles);
            // A provider may hide or dispose this group while its registrations are being sampled.
            if (_disposed || !_visible || _content == null) return;
            if (_membershipDirty)
            {
                _activePanels.Clear();
                foreach (var panel in _panels.Values)
                    if (panel.Active) _activePanels.Add(panel);
                _activePanels.Sort(ComparePanels);
                _membershipDirty = false;
            }
            if (_activePanels.Count == 0) { Hide(); return; }
            foreach (var panel in _activePanels) if (!panel.Ensure(_content)) return;

            if (nativeRect.HasValue) _obstacles.Add(nativeRect.Value);
            if (_preferPopupAnchor) _obstacles.Add(anchor);
            var fixedPanelWidth = 1d;
            foreach (var panel in _activePanels)
            {
                if (!panel.PrepareNatural()) { Hide(); return; }
                if (!panel.ExpandsToContent) fixedPanelWidth = Math.Max(fixedPanelWidth, panel.NaturalWidth);
            }
            var availableWidth = Math.Max(1, viewport.Width - 2 * ClientLayout.TooltipLayoutEngine.DefaultMargin);
            var defaultCharacters = ClientLayout.TooltipContentCache<RichText>.FitCharacters(fixedPanelWidth, availableWidth);
            _measures.Clear();
            foreach (var panel in _activePanels)
            {
                if (!panel.ReflowTo(availableWidth, defaultCharacters)) { Hide(); return; }
                _measures.Add(panel.Measure());
            }
            var result = ClientLayout.TooltipLayoutEngine.Arrange(anchor, viewport, _measures, _obstacles, _direction,
                preferHorizontal: _preferPopupAnchor);

            _direction = result.Direction;
            _scroll.Update(_source, result.ContentHeight, result.VisibleHeight, 12);
            PositionGroup(parent, result.ViewportBounds, result.ContentHeight, parentLeft, parentTop);
            for (var index = 0; index < result.Panels.Count; index++)
            {
                var placement = result.Panels[index];
                _panels[placement.Id].ShowAt(result.ViewportBounds, placement.Bounds);
            }
            SetScrollVisible(result.IsClipped || _scroll.IsOverflowing);
            _layoutFrame.Complete();
        }

        private static int ComparePanels(TooltipPanel left, TooltipPanel right) => left.Order != right.Order
            ? left.Order.CompareTo(right.Order)
            : string.CompareOrdinal(left.Id, right.Id);

        private void EnsureGroupRoot(RectTransform parent)
        {
            if (!ReferenceEquals(_root, null) && (_root == null || _root.parent != parent))
            {
                foreach (var panel in _panels.Values) panel.DestroyWindow();
                DestroyGroupRoot();
            }
            if (_root != null) return;
            _root = new GameObject("Digital Storage Tooltip Group", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(parent, false);
            _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _viewport = new GameObject("Tooltip Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            _viewport.SetParent(_root, false);
            _viewport.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            _content = new GameObject("Tooltip Content", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_viewport, false);
            _scrollIndicator = new GameObject("Tooltip Scroll Position", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _scrollIndicator.transform.SetParent(_viewport, false);
            _scrollIndicator.sprite = RenderHandler.current?.fill;
            _scrollIndicator.raycastTarget = false;
            _scrollHint = new GameObject("Tooltip Scroll Hint", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            _scrollHint.transform.SetParent(_viewport, false);
            _scrollHint.raycastTarget = false;
            _scrollHint.alignment = TextAlignmentOptions.BottomRight;
            _scrollHint.fontSize = 10;
            _scrollHint.text = ModHelper.GetLocalized("digital_storage", "tooltip_scroll_hint");
            _service.EnsureDriver();
        }

        private void PositionGroup(RectTransform parent, ClientLayout.TooltipRect bounds, double contentHeight,
            double parentLeft, double parentTop)
        {
            if (_root == null || _viewport == null || _content == null) return;
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0, 1);
            var anchor = new Vector2(parent.rect.xMin, parent.rect.yMax);
            var desired = new Vector2((float)(parentLeft + bounds.X), (float)(parentTop - bounds.Y));
            _root.anchoredPosition = desired - anchor;
            _root.sizeDelta = new Vector2((float)bounds.Width, (float)bounds.Height);
            _viewport.anchorMin = _viewport.anchorMax = _viewport.pivot = new Vector2(0, 1);
            _viewport.anchoredPosition = Vector2.zero;
            _viewport.sizeDelta = new Vector2((float)bounds.Width, (float)bounds.Height);
            _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0, 1);
            _content.sizeDelta = new Vector2((float)bounds.Width, (float)Math.Max(bounds.Height, contentHeight));
            _content.anchoredPosition = new Vector2(0, (float)_scroll.Offset);
            _root.SetAsLastSibling();
        }

        private void SetScrollVisible(bool visible)
        {
            if (_scrollIndicator == null || _scrollHint == null || _viewport == null || _scrollVisible == visible) return;
            _scrollVisible = visible;
            _scrollIndicator.enabled = visible;
            _scrollHint.enabled = visible;
            _scrollIndicator.rectTransform.anchorMin = _scrollIndicator.rectTransform.anchorMax = new Vector2(1, 0);
            _scrollIndicator.rectTransform.pivot = new Vector2(1, 0);
            _scrollIndicator.rectTransform.anchoredPosition = new Vector2(-2, 2);
            _scrollIndicator.rectTransform.sizeDelta = new Vector2(2, 20);
            _scrollHint.rectTransform.anchorMin = Vector2.zero;
            _scrollHint.rectTransform.anchorMax = Vector2.one;
            _scrollHint.rectTransform.offsetMin = new Vector2(4, 2);
            _scrollHint.rectTransform.offsetMax = new Vector2(-4, 2);
        }

        private void DestroyGroupRoot()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            _viewport = null;
            _content = null;
            _scrollIndicator = null;
            _scrollHint = null;
            _scrollVisible = null;
        }

        private RectTransform? ResolveParent() => RenderHandler.current?.overlayRoot?.rectTransform;
        private static ClientLayout.TooltipRect ScreenAnchor(RectTransform parent, Canvas? canvas, Vector2 screen,
            double parentLeft, double parentTop)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen,
                canvas?.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas?.worldCamera, out var point);
            var local = new ClientLayout.TooltipRect(point.x, point.y, 0, 0);
            return ClientLayout.TooltipCoordinateMapper.ToTopLeft(local, parentLeft, parentTop);
        }

        internal ClientLayout.TooltipRect LayoutRect(RectTransform parent, RectTransform child,
            double parentLeft, double parentTop) =>
            ClientLayout.TooltipCoordinateMapper.ToTopLeft(LocalBounds(parent, child), parentLeft, parentTop);

        private static ClientLayout.TooltipRect NativeWindowRect(PixelWindow window, RectTransform parent,
            double parentLeft, double parentTop)
        {
            var topLeft = parent.InverseTransformPoint(window.rectTransform.position);
            var scale = window.rectTransform.lossyScale;
            var parentScale = parent.lossyScale;
            var sx = Math.Abs(parentScale.x) > 0.0001f ? Math.Abs(scale.x / parentScale.x) : 1d;
            var sy = Math.Abs(parentScale.y) > 0.0001f ? Math.Abs(scale.y / parentScale.y) : 1d;
            var width = Math.Max(1, window.widthPixels) * sx;
            var height = Math.Max(1, window.heightPixels) * sy;
            var local = new ClientLayout.TooltipRect(topLeft.x, topLeft.y - height, width, height);
            return ClientLayout.TooltipCoordinateMapper.ToTopLeft(local, parentLeft, parentTop);
        }

        private static ClientLayout.TooltipRect CanvasFrame(RectTransform parent, Canvas? canvas)
        {
            canvas = canvas?.rootCanvas;
            if (canvas == null) return new ClientLayout.TooltipRect(0, 0, 0, 0);
            var pixel = canvas.pixelRect;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var index = 0; index < 4; index++)
            {
                var corner = new Vector3((index & 1) == 0 ? pixel.xMin : pixel.xMax,
                    index < 2 ? pixel.yMin : pixel.yMax, 0f);
                var world = corner;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
                    world = canvas.worldCamera.ScreenToWorldPoint(new Vector3(corner.x, corner.y, canvas.planeDistance));
                var point = parent.InverseTransformPoint(world);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            if (!float.IsFinite(min.x) || !float.IsFinite(min.y) || !float.IsFinite(max.x) || !float.IsFinite(max.y))
                return new ClientLayout.TooltipRect(0, 0, 0, 0);
            return new ClientLayout.TooltipRect(min.x, min.y, Math.Max(0, max.x - min.x), Math.Max(0, max.y - min.y));
        }

        private ClientLayout.TooltipRect LocalBounds(RectTransform parent, RectTransform child)
        {
            child.GetWorldCorners(_corners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in _corners)
            {
                var point = parent.InverseTransformPoint(corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return new ClientLayout.TooltipRect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        private void ThrowIfDisposed()
        {
            _service.ThrowIfWrongThread();
            if (_disposed) throw new ObjectDisposedException(nameof(TooltipGroup));
        }
    }

    internal sealed class TooltipPanel : ITooltipPanel, IDisposable
    {
        private const int HorizontalPadding = 16;
        private const int VerticalPadding = 16;
        private readonly TooltipGroup _group;
        private readonly string _id;
        private string? _text;
        private RichTextBuilder? _builder;
        private TooltipStyle _style = TooltipStyle.Native;
        private readonly ClientLayout.TooltipContentCache<RichText> _cache;
        private bool _styleDirty = true;
        private bool _styled;
        private bool _shown;
        private bool _plain;
        private bool _disposed;
        private readonly List<ImageState> _images = new();
        private readonly List<TextState> _texts = new();
        internal TooltipPanel(TooltipGroup group, string id, int order)
        {
            _group = group;
            _id = id;
            Order = order;
            _cache = new ClientLayout.TooltipContentCache<RichText>(BuildContent, RichTextComparer.Instance);
        }
        internal int Order { get; }
        internal bool Active { get; set; } = true;
        internal bool ExpandsToContent { get; private set; }
        internal double NaturalWidth => _cache.NaturalWidth;
        internal string Id => _id;
        internal PixelWindow? Window { get; private set; }
        internal RichTextElement? Element { get; private set; }
        internal bool Ensure(RectTransform parent)
        {
            if (Window?.handler != null && Window.handler.rectTransform.parent == parent) return true;
            if (Window != null) DestroyWindow();
            Window = new PixelWindow();
            Window.handler.name = "Digital Storage Tooltip Panel";
            Window.handler.rectTransform.SetParent(parent, false);
            Element = new RichTextElement(canResize: false, -1, -1, hasRaycast: false);
            Element.SetPadding(HorizontalPadding / 2, HorizontalPadding / 2, VerticalPadding / 2, VerticalPadding / 2);
            Window.Attach(Element);
            CaptureNativeStyle();
            _cache.InvalidateMetrics();
            _styleDirty = true;
            return true;
        }

        internal ClientLayout.TooltipPanelMeasure Measure() =>
            new(_id, Order, Math.Max(1, Window?.widthPixels ?? 1), Math.Max(1, Window?.heightPixels ?? 1));

        private int HorizontalChrome => HorizontalPadding + 2 * (Window?.xPadding ?? 0);

        internal bool PrepareNatural()
        {
            if (Element == null || Window == null) return false;
            try
            {
                var settings = UISettings.current;
                var text = Element.GetText().text;
                var locale = LocalizationSettings.SelectedLocaleAsync;
                _cache.UpdateMetrics(Element, text.font, locale.IsValid() && locale.IsDone ? locale.Result : null,
                    settings?.GetCharWidthScale() ?? 1, settings?.effectiveFontWidth ?? 1,
                    settings?.fontHeight ?? 1, text.fontSize);
                if (_cache.RefreshNatural())
                {
                    if (ExpandsToContent)
                    {
                        var width = text.GetPreferredValues(_cache.Natural.richTextForeground).x;
                        _cache.RecordNaturalWidth(Math.Ceiling(width) + HorizontalChrome);
                    }
                    else
                    {
                        ApplyContent(_cache.Natural, _cache.NaturalCharacters);
                        _cache.RecordNaturalWidth(Math.Max(1, Window.widthPixels));
                    }
                }
                return true;
            }
            catch (Exception error) { LogContentError(error); return false; }
        }

        internal bool ReflowTo(double availableWidth, int defaultCharacters)
        {
            if (Element == null || Window == null) return false;
            try
            {
                if (ExpandsToContent)
                    ApplyExpandedContent(_cache.Natural, _cache.FitContent(availableWidth, HorizontalChrome));
                else
                    ApplyContent(_cache.Fitted(defaultCharacters), defaultCharacters);
                return true;
            }
            catch (Exception error) { LogContentError(error); return false; }
        }

        internal void SetExpandToContent(bool expand)
        {
            ThrowIfDisposed();
            if (ExpandsToContent == expand) return;
            ExpandsToContent = expand;
            _cache.SetExpandToContent(expand);
            _group.InvalidateLayout();
        }

        internal void ShowAt(ClientLayout.TooltipRect viewport, ClientLayout.TooltipRect bounds)
        {
            if (Window == null || Element == null) return;
            if (!_shown || !Window.IsVisible()) Window.Show();
            _shown = true;
            var rect = Window.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2((float)(bounds.X - viewport.X),
                (float)(viewport.Y - bounds.Y));
            ApplyStyle();
        }

        internal void Hide()
        {
            if (Window?.handler != null && Window.IsVisible()) Window.Hide();
            _shown = false;
        }
        internal void DestroyWindow()
        {
            if (Window == null) return;
            Hide();
            Window.Destroy();
            GraphHandler.windowGraphNodeRoots?.Remove(Window);
            WindowsHandler.current?.visibleWindows?.Remove(Window);
            Window = null;
            Element = null;
            _images.Clear();
            _texts.Clear();
            _styled = false;
            _cache.InvalidateMetrics();
        }

        public void SetText(string text)
        {
            ThrowIfDisposed();
            text ??= string.Empty;
            if (_plain && string.Equals(_text, text, StringComparison.Ordinal)) return;
            _text = text;
            _builder = null;
            _plain = true;
            _styleDirty = true;
            _cache.Submit();
            _group.InvalidateLayout();
        }

        public void SetRichText(RichTextBuilder builder)
        {
            ThrowIfDisposed();
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _text = null;
            if (_plain) _styleDirty = true;
            _plain = false;
            _cache.Submit();
            _group.InvalidateLayout();
        }

        public void SetStyle(TooltipStyle style)
        {
            ThrowIfDisposed();
            if (style == null) throw new ArgumentNullException(nameof(style));
            if (_style.Kind == style.Kind && _style.BorderRgb == style.BorderRgb &&
                _style.BackgroundRgb == style.BackgroundRgb && _style.TextRgb == style.TextRgb) return;
            _style = style;
            _styleDirty = true;
            _group.InvalidateLayout();
        }

        public void Dispose()
        {
            _group.Service.ThrowIfWrongThread();
            if (_disposed) return;
            _disposed = true;
            DestroyWindow();
        }

        private RichText BuildContent(int charWidth) => _builder == null
            ? new RichTextBuilder(charWidth).AddLine(_text ?? string.Empty, ignoreEmpty: false).Build()
            : new RichTextBuilder(charWidth).Add(_builder).Build();

        private void ApplyExpandedContent(RichText rich, int textWidth)
        {
            if (Element == null || Window == null || !_cache.NeedsApply(rich, textWidth)) return;
            RestoreNativeStyle();
            Element.SetRichText(rich, _cache.NaturalCharacters);
            var text = Element.GetText().text;
            text.enableWordWrapping = true;
            var textHeight = Mathf.CeilToInt(text.GetPreferredValues(textWidth, 32767).y);
            Element.SetFixedSize(textWidth + HorizontalPadding, textHeight + VerticalPadding);
            Window.Validate();
            // Native validation sizes the text to the full element, including padding. TMP wraps inside the text area.
            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
            RecordContent(rich, textWidth);
        }

        private void ApplyContent(RichText rich, int charWidth)
        {
            if (Element == null || Window == null || !_cache.NeedsApply(rich, charWidth)) return;
            RestoreNativeStyle();
            if (Element.fixedWidth >= 0) Element.SetFixedSize(-1, -1);
            Element.SetRichText(rich, charWidth);
            Window.Validate();
            RecordContent(rich, charWidth);
        }

        private void RecordContent(RichText rich, int width)
        {
            _cache.RecordApplied(rich, width);
            // Validate resets some native image colors. Capture only after restoring the untinted baseline.
            CaptureNativeStyle();
            _styleDirty = true;
        }

        private void LogContentError(Exception error) =>
            Debug.LogWarning($"Digital Storage: tooltip panel {_id} failed ({error.GetType().Name}).");

        private void RestoreNativeStyle()
        {
            if (!_styled) return;
            foreach (var image in _images) image.Restore();
            foreach (var text in _texts) text.Restore();
            _styled = false;
        }

        private void ThrowIfDisposed()
        {
            _group.Service.ThrowIfWrongThread();
            if (_disposed) throw new ObjectDisposedException(nameof(TooltipPanel));
        }

        private sealed class RichTextComparer : IEqualityComparer<RichText>
        {
            internal static readonly RichTextComparer Instance = new();
            public bool Equals(RichText? left, RichText? right) => ReferenceEquals(left, right) ||
                (left != null && right != null && left.recommendedWidth == right.recommendedWidth &&
                left.recommendedHeight == right.recommendedHeight && left.isTruncated == right.isTruncated &&
                string.Equals(left.nakedText, right.nakedText, StringComparison.Ordinal) &&
                string.Equals(left.richTextForeground, right.richTextForeground, StringComparison.Ordinal) &&
                string.Equals(left.richTextBackground, right.richTextBackground, StringComparison.Ordinal) &&
                string.Equals(left.richTextUnderline, right.richTextUnderline, StringComparison.Ordinal) &&
                string.Equals(left.richTextStrikethrough, right.richTextStrikethrough, StringComparison.Ordinal));
            public int GetHashCode(RichText value) => value.GetHashCode();
        }

        private void CaptureNativeStyle()
        {
            if (Window == null) return;
            _images.Clear();
            foreach (var image in Window.handler.GetComponentsInChildren<Image>(true))
                _images.Add(new ImageState(image));
            _texts.Clear();
            foreach (var text in Window.handler.GetComponentsInChildren<TMP_Text>(true))
                _texts.Add(new TextState(text));
        }

        private void ApplyStyle()
        {
            if (Window == null || !_styleDirty) return;
            RestoreNativeStyle();
            _styleDirty = false;
            if (_style.Kind != TooltipStyleKind.Rgb) return;
            _styled = true;
            var frame = _group.Service.RgbFrame;
            var nativeOutside = RenderHandler.current?.outside;
            var nativeInside = RenderHandler.current?.inside;
            var nativeFill = RenderHandler.current?.fill;
            foreach (var image in _images)
            {
                if (frame != null && (image.Sprite == nativeOutside || image.Sprite == nativeInside))
                {
                    image.Image.sprite = frame;
                    image.Image.type = Image.Type.Sliced;
                    image.Image.fillCenter = false;
                    if (_style.BorderRgb.HasValue) image.Image.color = ToColor(_style.BorderRgb.Value);
                }
                else if (image.Sprite == nativeFill && _style.BackgroundRgb.HasValue)
                    image.Image.color = ToColor(_style.BackgroundRgb.Value);
            }
            if (_plain && _style.TextRgb.HasValue && Element != null)
                Element.GetText().text.color = ToColor(_style.TextRgb.Value);
        }

        private static Color ToColor(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private sealed class ImageState
        {
            internal ImageState(Image image)
            {
                Image = image;
                Sprite = image.sprite;
                Type = image.type;
                Multiplier = image.pixelsPerUnitMultiplier;
                FillCenter = image.fillCenter;
                Color = image.color;
                Enabled = image.enabled;
            }
            internal Image Image { get; }
            internal Sprite? Sprite { get; }
            private Image.Type Type { get; }
            private float Multiplier { get; }
            private bool FillCenter { get; }
            private Color Color { get; }
            private bool Enabled { get; }
            internal void Restore()
            {
                if (Image == null) return;
                Image.sprite = Sprite;
                Image.type = Type;
                if (Image.pixelsPerUnitMultiplier != Multiplier) Image.pixelsPerUnitMultiplier = Multiplier;
                Image.fillCenter = FillCenter;
                Image.color = Color;
                Image.enabled = Enabled;
            }
        }

        private sealed class TextState
        {
            internal TextState(TMP_Text text) { Text = text; Color = text.color; }
            internal TMP_Text Text { get; }
            private Color Color { get; }
            internal void Restore() { if (Text != null) Text.color = Color; }
        }
    }
}

internal sealed class TooltipServiceDriver : MonoBehaviour
{
    private NativeTooltipService? _service;
    internal void Initialize(NativeTooltipService service)
    {
        _service = service;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    internal void Detach()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        _service = null;
        enabled = false;
    }

    private void LateUpdate() => _service?.Tick(this);
    private void OnDisable() => _service?.DriverDisabled(this);
    private void OnSceneUnloaded(Scene scene) => _service?.SceneUnloaded(this);

    private void OnDestroy()
    {
        if (_service == null) return;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        var service = _service;
        _service = null;
        service.DriverDestroyed(this);
    }
}
