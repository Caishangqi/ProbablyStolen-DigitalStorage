using System;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal static class LogSettingsRowEdit
{
    internal static void Apply(Action localize, Action group, Action restoreLabels, Action restoreGroup)
    {
        try
        {
            localize();
            group();
        }
        catch
        {
            try { restoreGroup(); }
            catch { }
            try { restoreLabels(); }
            catch { }
        }
    }
}
