using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class BuiltinLogCategories
{
    internal BuiltinLogCategories()
    {
        Storage = Registry.Register("log_storage", "log_category_storage", "Storage log", LogVerbosity.Info, true);
        Transfer = Registry.Register("log_transfer", "log_category_transfer", "Transfer log", LogVerbosity.Info, true);
        Comp = Registry.Register("log_comp", "log_category_comp", "Compatibility log", LogVerbosity.Info, true);
        Temp = Registry.Register("log_temp", "log_category_temp", "Temporary log", LogVerbosity.Verbose, false);
        Registry.Freeze();
    }

    internal LogCategories Registry { get; } = new();
    internal LogCategory Storage { get; }
    internal LogCategory Transfer { get; }
    internal LogCategory Comp { get; }
    internal LogCategory Temp { get; }
}
