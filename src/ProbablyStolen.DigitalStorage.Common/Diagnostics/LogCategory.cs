using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Diagnostics;

internal sealed class LogCategory
{
    internal LogCategory(string id, string? labelKey, string fallbackLabel, LogVerbosity defaultVerbosity, bool defaultEnabled)
    {
        Id = id;
        Name = string.Concat(id.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        LabelKey = labelKey;
        FallbackLabel = fallbackLabel;
        DefaultVerbosity = defaultVerbosity;
        DefaultEnabled = defaultEnabled;
    }

    internal string Id { get; }
    internal string Name { get; }
    internal string? LabelKey { get; }
    internal string FallbackLabel { get; }
    internal LogVerbosity DefaultVerbosity { get; }
    internal bool DefaultEnabled { get; }
}
