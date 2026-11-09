using System.Globalization;
using TMPro;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Storage-free machinery controls for the drive's existing priority tag.</summary>
internal sealed class DrivePriorityWindow
{
    private const int ControlWidth = 32;
    private const int ControlHeight = 16;
    private readonly GameItem _drive;
    private readonly EmptyPixelElement _fieldHost = new(ControlWidth, ControlHeight);
    private TMP_InputField? _field;

    internal DrivePriorityWindow(GameItem drive)
    {
        _drive = drive;
        Content = MachineryStack.Vertical(
            Button(100), Button(10), Button(1), _fieldHost,
            Button(-1), Button(-10), Button(-100));
    }

    internal MachineryStack Content { get; }

    internal void Refresh()
    {
        if (_field == null && CustomUIManager.Instance != null)
        {
            // EmptyPixelElement tracks pixel dimensions but does not apply them to its RectTransform.
            _fieldHost.rectTransform.sizeDelta = new Vector2(ControlWidth, ControlHeight);
            var root = CustomUIManager.Instance.SpawnElement(CustomUIManager.ElementKind.Input,
                _fieldHost.handler.transform, null, "digital_storage_priority_input");
            if (root != null)
            {
                var rect = root.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                _field = root.GetComponentInChildren<TMP_InputField>(true);
                if (_field != null)
                {
                    _field.pointSize = 7;
                    if (_field.placeholder is TMP_Text placeholder) placeholder.text = string.Empty;
                    if (_field.textComponent != null)
                    {
                        _field.textComponent.alignment = TextAlignmentOptions.Center;
                        _field.textComponent.enableAutoSizing = false;
                    }
                    if (_field.textViewport != null)
                    {
                        _field.textViewport.anchorMin = Vector2.zero;
                        _field.textViewport.anchorMax = Vector2.one;
                        _field.textViewport.offsetMin = new Vector2(2, 1);
                        _field.textViewport.offsetMax = new Vector2(-2, -1);
                    }
                    _field.onValueChanged.AddListener(Typed);
                    _field.onEndEdit.AddListener(_ => ShowCurrent());
                }
                else
                {
                    Object.Destroy(root);
                }
            }
        }
        ShowCurrent();
    }

    private MachineryButton Button(int delta) =>
        new MachineryButton(delta.ToString("+0;-0", CultureInfo.InvariantCulture), () => Step(delta), ControlWidth, ControlHeight)
            .SetFillWidth(false);

    private void Typed(string text)
    {
        if (!DrivePriority.TryParse(text?.Trim(), out var priority)) return;
        DrivePriorityTag.Write(_drive, priority);
        if (!long.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var raw)
            || raw != priority) ShowCurrent();
    }

    private void Step(int delta)
    {
        DrivePriorityTag.Write(_drive, DrivePriority.Clamp((long)DrivePriorityTag.Read(_drive) + delta));
        ShowCurrent();
    }

    private void ShowCurrent() => _field?.SetTextWithoutNotify(
        DrivePriorityTag.Read(_drive).ToString(CultureInfo.InvariantCulture));
}
