using System;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal enum PinyinSearchMode
{
    Auto = 0,
    On = 1,
    Off = 2,
}

internal static class PinyinSearchModePolicy
{
    internal const string Key = "terminal_search_pinyin_mode";
    internal static readonly string[] Choices = { "Auto", "On", "Off" };

    internal static PinyinSearchMode Normalize(PinyinSearchMode mode) =>
        mode == PinyinSearchMode.On || mode == PinyinSearchMode.Off ? mode : PinyinSearchMode.Auto;

    internal static bool IsEnabled(PinyinSearchMode mode, string? locale)
    {
        mode = Normalize(mode);
        return mode == PinyinSearchMode.On || (mode == PinyinSearchMode.Auto && IsChinese(locale));
    }

    internal static bool HasCanonicalChoices(string[]? choices) =>
        choices != null && choices.Length == 3 && choices[0] == Choices[0]
        && choices[1] == Choices[1] && choices[2] == Choices[2];

    internal static bool IsValidIndex(int index) => index >= 0 && index < 3;

    private static bool IsChinese(string? locale) =>
        locale != null && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        && (locale.Length == 2 || (locale.Length > 3 && (locale[2] == '-' || locale[2] == '_')));
}
