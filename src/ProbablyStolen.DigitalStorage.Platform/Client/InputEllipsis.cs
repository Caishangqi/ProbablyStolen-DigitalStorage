using TMPro;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

// TMP_InputField scrolls long values without an ellipsis, so an idle field uses a separate label.
// Enter or Esc can end editing without deselecting the field. Checking isFocused also catches a later click
// that resumes editing without a new select event.
internal sealed class InputEllipsis : MonoBehaviour
{
    private TMP_InputField? _input;
    private TMP_Text? _source;
    private TMP_Text? _label;
    private bool? _editing;

    internal static void Attach(CustomUIElement element)
    {
        var input = element.input;
        var source = input == null ? null : input.textComponent;
        if (input == null || source == null || source.rectTransform.parent is not RectTransform area) return;

        var copy = Instantiate(source.gameObject, area, false);
        copy.name = "digital_storage_input_ellipsis";
        var label = copy.GetComponent<TMP_Text>();
        if (label == null)
        {
            Destroy(copy);
            return;
        }

        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = source.rectTransform.pivot;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        var view = input.gameObject.AddComponent<InputEllipsis>();
        view._input = input;
        view._source = source;
        view._label = label;
        view.LateUpdate();
    }

    private void LateUpdate()
    {
        if (_input == null || _source == null || _label == null) return;
        var editing = _input.isFocused;
        if (!editing && _label.text != _input.text) _label.text = _input.text;
        if (_editing == editing) return;
        _editing = editing;
        _source.enabled = editing;
        _label.enabled = !editing;
    }
}
