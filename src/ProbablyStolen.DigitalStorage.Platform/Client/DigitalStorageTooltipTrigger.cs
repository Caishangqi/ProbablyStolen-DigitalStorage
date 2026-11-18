using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class DigitalStorageTooltipTrigger : CustomUITooltipTrigger, IPointerEnterHandler, IPointerExitHandler
{
    private NativeTooltipService? _buttonService;
    private CustomUIManager? _nativeFallback;

    internal bool IsButton => _buttonService != null;

    internal void UseButtonTooltip(NativeTooltipService service) => _buttonService = service;

    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
    {
        if (_buttonService != null)
        {
            HideNativeFallback();
            if (string.IsNullOrEmpty(tooltipText) || _buttonService.TryShowButtonTooltip(this)) return;
            _nativeFallback = CustomUIManager.Instance;
        }
        base.OnPointerEnter(eventData);
    }

    void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
    {
        if (_buttonService == null)
            base.OnPointerExit(eventData);
        else
            HideButtonTooltip();
    }

    private void OnDisable()
    {
        if (_buttonService == null)
            CustomUIManager.Instance?.HideTooltip(this);
        else
            HideButtonTooltip();
    }

    private void HideButtonTooltip()
    {
        _buttonService?.HideButtonTooltip(this);
        HideNativeFallback();
    }

    private void HideNativeFallback()
    {
        var manager = _nativeFallback;
        _nativeFallback = null;
        if (manager != null && this != null) manager.HideTooltip(this);
    }
}
