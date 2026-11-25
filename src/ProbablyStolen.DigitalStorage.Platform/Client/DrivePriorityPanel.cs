using System.Globalization;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using TMPro;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The drive's native priority panel, titled like the game's own machine panels: step buttons above and below the value
/// field, then how the priority orders storing and taking. It lives in the drive's native window, and the drive window's
/// priority page borrows it. Every change writes the priority tag at once; text that is not a whole number keeps the old
/// value.
/// </summary>
internal sealed class DrivePriorityPanel
{
    internal const string Role = "priority";
    private const int ButtonWidth = 36;
    private const int ControlHeight = 16;
    private const int Gap = 4;
    private static readonly int[] Steps = { 1, 10, 100, 1000 };
    private static readonly ConditionalWeakTable<GameItem, DrivePriorityPanel> Panels = new();
    private readonly GameItem _drive;
    private readonly EmptyPixelElement _fieldHost = new(RowWidth, ControlHeight);
    private TMP_InputField? _field;

    private DrivePriorityPanel(GameItem drive, ModText text)
    {
        _drive = drive;
        var content = MachineryStack.Vertical(Row(1), _fieldHost, Row(-1),
            Note(text.Get("priority_insert_rule")), Note(text.Get("priority_extract_rule")));
        Panel = MachineHelper.Panel(text.Get("priority_title"), content, Role);
    }

    internal MachineryPanel Panel { get; }

    private static int RowWidth => Steps.Length * ButtonWidth + (Steps.Length - 1) * Gap;

    /// <summary>Creates the panel of a drive and remembers it for the drive's priority page.</summary>
    internal static DrivePriorityPanel Create(GameItem drive, ModText text)
    {
        var panel = new DrivePriorityPanel(drive, text);
        Panels.AddOrUpdate(drive, panel);
        return panel;
    }

    /// <summary>Gets the panel of a drive, or null for a drive created without one.</summary>
    internal static DrivePriorityPanel? Of(GameItem drive) => Panels.TryGetValue(drive, out var panel) ? panel : null;

    /// <summary>Creates the value field on first use and shows the drive's current priority.</summary>
    internal void Refresh()
    {
        if (_field == null && CustomUIManager.Instance != null) CreateField();
        ShowCurrent();
    }

    private void CreateField()
    {
        // EmptyPixelElement tracks pixel dimensions but does not apply them to its RectTransform.
        _fieldHost.rectTransform.sizeDelta = new Vector2(RowWidth, ControlHeight);
        var root = CustomUIManager.Instance.SpawnElement(CustomUIManager.ElementKind.Input,
            _fieldHost.handler.transform, null, "digital_storage_priority_input");
        if (root == null) return;
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _field = root.GetComponentInChildren<TMP_InputField>(true);
        if (_field == null)
        {
            Object.Destroy(root);
            return;
        }

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

        _field.onEndEdit.AddListener(Typed);
    }

    private MachineryStack Row(int sign)
    {
        var buttons = new PixelElement[Steps.Length];
        for (var index = 0; index < Steps.Length; index++)
        {
            var delta = sign * Steps[index];
            buttons[index] = new MachineryButton(delta.ToString("+0;-0", CultureInfo.InvariantCulture), () => Step(delta),
                ButtonWidth, ControlHeight).SetFillWidth(false);
        }

        return MachineryStack.Horizontal(buttons);
    }

    private static MachineryText Note(string text) =>
        new MachineryText(text, alignment: HorizontalAlignmentOptions.Left, uppercase: false).SetFillWidth(true);

    private void Step(int delta)
    {
        DrivePriorityTag.Write(_drive, DrivePriority.Clamp((long)DrivePriorityTag.Read(_drive) + delta));
        ShowCurrent();
    }

    private void Typed(string text)
    {
        if (DrivePriority.TryParse(text, out var priority)) DrivePriorityTag.Write(_drive, priority);
        ShowCurrent();
    }

    private void ShowCurrent() =>
        _field?.SetTextWithoutNotify(DrivePriorityTag.Read(_drive).ToString(CultureInfo.InvariantCulture));
}
