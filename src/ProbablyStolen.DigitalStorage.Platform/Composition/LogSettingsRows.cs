using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal static class LogSettingsRows
{
    internal static void Group(CustomUIWindow window, LogSetting setting, ConfigEntry<LogVerbosity> verbosity,
        ConfigEntry<bool> enabled, Func<string, string?> translate)
    {
        var dropdown = window.Get(verbosity.Key);
        var toggle = window.Get(enabled.Key);
        var label = window.Get("label_" + verbosity.Key);
        var toggleLabel = window.Get("label_" + enabled.Key);
        if (dropdown is not { IsAlive: true } || toggle is not { IsAlive: true } || label is not { IsAlive: true } ||
            toggleLabel is not { IsAlive: true } || dropdown.dropdown == null || toggle.toggle == null ||
            label.text == null || toggleLabel.text == null || verbosity.Choices is not { } choices) return;

        var menu = dropdown.dropdown;
        var destination = dropdown.root.transform.parent;
        var control = toggle.root.transform;
        var source = control.parent;
        if (source == null || destination == null || source.parent == null || source.parent != destination.parent ||
            source.GetSiblingIndex() != destination.GetSiblingIndex() + 1 || !IsEntryRow(source, toggleLabel.root.transform, control) ||
            !IsEntryRow(destination, label.root.transform, dropdown.root.transform) || menu.options.Count != choices.Length) return;

        var original = menu.options.Select(option => option.text).ToArray();
        if (!original.SequenceEqual(choices, StringComparer.Ordinal)) return;
        var index = dropdown.GetIndex();
        var selected = setting.DisplayedIndex(verbosity.Value, index, choices);
        if (index < 0 || index >= choices.Length || selected < 0 || selected >= choices.Length) return;
        var labels = choices.Select(choice => LogSetting.Localized(translate, "log_level_" + choice.ToLowerInvariant(), choice)).ToArray();
        var sourceVisible = source.gameObject.activeSelf;
        var controlIndex = control.GetSiblingIndex();
        // Apply and Reset find the live controls by ID. Keep their instances and localize only the displayed choices.
        void SetLabels(string[] values, int selectedIndex)
        {
            for (var i = 0; i < values.Length; i++) menu.options[i].text = values[i];
            dropdown.SetIndex(selectedIndex, notify: false);
            menu.RefreshShownValue();
        }

        LogSettingsRowEdit.Apply(() => SetLabels(labels, selected), () =>
        {
            control.SetParent(destination, worldPositionStays: false);
            source.gameObject.SetActive(false);
        }, () => SetLabels(original, index), () =>
        {
            control.SetParent(source, worldPositionStays: false);
            control.SetSiblingIndex(controlIndex);
            source.gameObject.SetActive(sourceVisible);
        });
    }

    private static bool IsEntryRow(Transform row, Transform label, Transform control) =>
        row.name == "row" && row.childCount == 3 && row.GetComponent<HorizontalLayoutGroup>() != null &&
        row.GetChild(0) == label && row.GetChild(1).name == "flexible_space" && row.GetChild(2) == control;
}
