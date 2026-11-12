using TMPro;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

// Enter or Esc can end editing without deselection, so select events do not reliably report editing state.
internal sealed class InputEllipsis : MonoBehaviour
{
    private TMP_InputField? _input;
    private TMP_Text? _text;
    private TextOverflowModes _editingMode;
    private bool? _editing;
    // Layout resizes the field after Attach; refit only when that height changes.
    private float _fittedHeight = -1f;

    internal static void Attach(CustomUIElement element)
    {
        var input = element.input;
        var text = input == null ? null : input.textComponent;
        if (input == null || text == null || input.lineType != TMP_InputField.LineType.SingleLine) return;
        var view = input.gameObject.AddComponent<InputEllipsis>();
        view._input = input;
        view._text = text;
        view._editingMode = text.overflowMode;
        view.LateUpdate();
    }

    private void LateUpdate()
    {
        if (_input == null || _text == null) return;
        var editing = _input.isFocused;
        if (!editing && !Mathf.Approximately(_text.rectTransform.rect.height, _fittedHeight)) FitOneLine();
        if (_editing == editing) return;
        _editing = editing;
        if (editing)
        {
            _text.overflowMode = _editingMode;
            return;
        }

        _text.overflowMode = TextOverflowModes.Ellipsis;
        var rect = _text.rectTransform;
        rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
    }

    // TMP ellipsis hides the entire line when its text rect is too short.
    // Native input padding can cause this even when the field itself is tall enough.
    private void FitOneLine()
    {
        if (_text == null) return;
        var rect = _text.rectTransform;
        var missing = _text.GetPreferredValues("Ag").y - rect.rect.height;
        if (missing > 0f)
        {
            var half = Mathf.Ceil(missing / 2f) + 1f;
            rect.offsetMin -= new Vector2(0f, half);
            rect.offsetMax += new Vector2(0f, half);
        }

        _fittedHeight = rect.rect.height;
    }
}
