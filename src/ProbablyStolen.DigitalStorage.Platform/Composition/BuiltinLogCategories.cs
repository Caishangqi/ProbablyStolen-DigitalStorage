using ProbablyStolen.DigitalStorage.Common.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class BuiltinLogCategories
{
    internal BuiltinLogCategories()
    {
        Storage = Registry.Register("log_storage", "log_category_storage", "Storage log", LogVerbosity.Info, true);
        Transfer = Registry.Register("log_transfer", "log_category_transfer", "Transfer log", LogVerbosity.Info, true);
        Comp = Registry.Register("log_comp", "log_category_comp", "Compatibility log", LogVerbosity.Info, true);
        Content = Registry.Register("log_content", "log_category_content", "Content log", LogVerbosity.Info, true);
        Network = Registry.Register("log_network", "log_category_network", "Network log", LogVerbosity.Info, true);
        // Window traces are verbose; at the default level only interface errors and warnings are written.
        Ui = Registry.Register("log_ui", "log_category_ui", "Interface log", LogVerbosity.Warning, true);
        Temp = Registry.Register("log_temp", "log_category_temp", "Temporary log", LogVerbosity.Verbose, false);
        Registry.Freeze();
    }

    internal LogCategories Registry { get; } = new();
    internal LogCategory Storage { get; }
    internal LogCategory Transfer { get; }
    internal LogCategory Comp { get; }
    internal LogCategory Content { get; }
    internal LogCategory Network { get; }
    internal LogCategory Ui { get; }
    internal LogCategory Temp { get; }
}
