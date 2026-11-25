using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProbablyStolen.DigitalStorage.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class NativeButtonTooltip
{
    private const float PointerOffset = 12f;

    private CustomUIManager? _manager;
    private GameObject? _prefab;
    private Canvas? _overlay;
    private RectTransform? _popup;
    private TextMeshProUGUI? _label;
    private DigitalStorageTooltipTrigger? _owner;
    private TMP_FontAsset? _measuredFont;
    private string? _measuredText;
    private float _widestLine;
    private int _horizontalPadding;

    internal bool TryShow(DigitalStorageTooltipTrigger owner)
    {
        var manager = CustomUIManager.Instance;
        if (manager == null || manager.tooltipPrefab == null)
        {
            Release();
            return false;
        }

        if (_popup == null || _label == null || _overlay == null || _manager != manager || _prefab != manager.tooltipPrefab)
        {
            Release();
            if (!TryCreate(manager, manager.tooltipPrefab)) return false;
        }

        _owner = owner;
        Tick();
        return true;
    }

    internal void Hide(DigitalStorageTooltipTrigger owner)
    {
        if (ReferenceEquals(_owner, owner)) Hide();
    }

    internal void Hide()
    {
        _owner = null;
        SetHidden();
    }

    internal void Tick()
    {
        if (ReferenceEquals(_manager, null)) return;
        if (_manager == null || _manager != CustomUIManager.Instance || _overlay == null || _popup == null || _label == null)
        {
            Release();
            return;
        }
        if (_owner == null || !_owner.isActiveAndEnabled || string.IsNullOrEmpty(_owner.tooltipText))
        {
            if (!ReferenceEquals(_owner, null)) Hide();
            return;
        }
        UpdatePopup(_overlay, _popup, _label, _owner.tooltipText);
    }

    internal void Release()
    {
        Hide();
        if (_popup != null) Object.Destroy(_popup.gameObject);
        _popup = null;
        _label = null;
        _overlay = null;
        _manager = null;
        _prefab = null;
        _measuredFont = null;
        _measuredText = null;
    }

    private bool TryCreate(CustomUIManager manager, GameObject prefab)
    {
        if (prefab.transform is not RectTransform root || root.pivot != new Vector2(0f, 1f) || root.childCount != 1 ||
            !prefab.TryGetComponent<VerticalLayoutGroup>(out var layout) || !layout.enabled || !layout.childControlWidth ||
            !layout.childForceExpandWidth || !layout.childControlHeight ||
            !prefab.TryGetComponent<ContentSizeFitter>(out var fitter) || !fitter.enabled ||
            fitter.verticalFit != ContentSizeFitter.FitMode.PreferredSize ||
            !root.GetChild(0).TryGetComponent<TextMeshProUGUI>(out var label) || !label.enabled ||
            !label.gameObject.activeSelf || !label.enableWordWrapping)
            return false;

        var overlayRoot = manager.GetLayerRoot("overlay");
        if (overlayRoot == null || !overlayRoot.TryGetComponent<Canvas>(out var overlay)) return false;

        var clone = Object.Instantiate(prefab, overlayRoot, false);
        clone.name = "Digital Storage Button Tooltip";
        clone.SetActive(false);
        var canvasGroup = clone.GetComponent<CanvasGroup>() ?? clone.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        clone.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        _popup = (RectTransform)clone.transform;
        _label = _popup.GetChild(0).GetComponent<TextMeshProUGUI>();
        // The native prefab shrinks its text to fit; the hint grows to fit its text instead, at the largest size.
        if (_label.enableAutoSizing)
        {
            _label.enableAutoSizing = false;
            _label.fontSize = _label.fontSizeMax;
        }
        _horizontalPadding = layout.padding.horizontal;
        _manager = manager;
        _prefab = prefab;
        _overlay = overlay;
        return true;
    }

    private void UpdatePopup(Canvas overlay, RectTransform popup, TextMeshProUGUI label, string text)
    {
        var overlayScale = overlay.scaleFactor;
        var dock = LetterboxManager.DockCanvas;
        var dockRoot = LetterboxManager.DockRoot;
        var dockCamera = dock != null ? dock.worldCamera : null;
        var dockScale = dock != null ? overlayScale / dock.scaleFactor : 0f;
        var useDock = dock != null && dock.isActiveAndEnabled && dockRoot != null && dockRoot.gameObject.activeInHierarchy &&
                      dockCamera != null && dockCamera.isActiveAndEnabled && float.IsFinite(dockScale) && dockScale > 0f;
        var viewport = useDock
            ? new RectInt(0, 0, Screen.width, Screen.height)
            : new RectInt(LetterboxManager.GameAreaX, LetterboxManager.GameAreaY, LetterboxManager.GameAreaWidth, LetterboxManager.GameAreaHeight);
        if ((!useDock && !overlay.isActiveAndEnabled) || viewport.height <= 0 ||
            !ButtonTooltipWidth.TryFit(0f, _horizontalPadding, viewport.width, overlayScale, out _))
        {
            SetHidden();
            return;
        }

        var parent = useDock ? dockRoot! : (RectTransform)overlay.transform;
        var reparented = popup.parent != parent;
        if (reparented)
        {
            popup.SetParent(parent, false);
            popup.localPosition = new Vector3(popup.localPosition.x, popup.localPosition.y, 0f);
        }
        var scale = useDock ? Vector3.one * dockScale : Vector3.one;
        if (popup.localScale != scale) popup.localScale = scale;
        if (reparented || !popup.gameObject.activeSelf) popup.SetAsLastSibling();
        if (!popup.gameObject.activeSelf) popup.gameObject.SetActive(true);

        // The native localizer can change the font on activation or when the language changes.
        var textChanged = label.text != text;
        if (textChanged) label.text = text;
        var needsMeasure = _measuredText != text || _measuredFont != label.font;
        if (needsMeasure)
        {
            _widestLine = label.GetPreferredValues(text).x;
            _measuredText = text;
            _measuredFont = label.font;
        }
        if (!ButtonTooltipWidth.TryFit(_widestLine, _horizontalPadding, viewport.width, overlayScale, out var width))
        {
            SetHidden();
            return;
        }
        var widthChanged = width != popup.rect.width;
        if (widthChanged) popup.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        if (textChanged || needsMeasure || widthChanged) LayoutRebuilder.ForceRebuildLayoutImmediate(popup);
        Position(popup, parent, useDock ? dockCamera : null, viewport, overlayScale);
    }

    private static void Position(RectTransform popup, RectTransform parent, Camera? camera, RectInt viewport, float scale)
    {
        var pointer = Input.mousePosition;
        var size = popup.rect.size * scale;
        var screenPoint = new Vector2(
            Mathf.Clamp(pointer.x + PointerOffset, viewport.xMin, Mathf.Max(viewport.xMin, viewport.xMax - size.x)),
            Mathf.Clamp(pointer.y - PointerOffset, Mathf.Min(viewport.yMax, viewport.yMin + size.y), viewport.yMax));
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screenPoint, camera, out var world))
            popup.position = world;
    }

    private void SetHidden()
    {
        if (_popup != null && _popup.gameObject.activeSelf) _popup.gameObject.SetActive(false);
    }
}
