using System;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal static class PinyinDropdownDisplay
{
    private static bool _failureLogged;

    internal static bool Apply(string? modId, bool windowAlive, string? key, Type? valueType,
        string[]? canonicalChoices, int index, string[] labels, bool elementAlive, bool hasDropdown,
        Action<string[]> setOptions, Action<int> setIndex, Action refresh, Action<Exception> logFailure)
    {
        if (!string.Equals(modId, "digital_storage", StringComparison.Ordinal) || !windowAlive ||
            !string.Equals(key, PinyinSearchModePolicy.Key, StringComparison.Ordinal) ||
            valueType != typeof(PinyinSearchMode) || !PinyinSearchModePolicy.HasCanonicalChoices(canonicalChoices) ||
            !elementAlive || !hasDropdown || !PinyinSearchModePolicy.IsValidIndex(index) ||
            labels == null || labels.Length != PinyinSearchModePolicy.Choices.Length)
            return false;

        try
        {
            setOptions(labels);
            setIndex(index);
            refresh();
            return true;
        }
        catch (Exception error)
        {
            try
            {
                setOptions(PinyinSearchModePolicy.Choices);
                setIndex(index);
                refresh();
            }
            catch (Exception) { }
            if (!_failureLogged)
            {
                _failureLogged = true;
                try { logFailure(error); }
                catch (Exception) { }
            }
            return false;
        }
    }
}
