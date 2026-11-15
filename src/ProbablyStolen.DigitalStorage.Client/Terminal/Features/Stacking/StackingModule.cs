using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>Identifiers of the stack display module and the terminal data it owns.</summary>
public static class StackingModule
{
    /// <summary>The module item identifier.</summary>
    public const string ModuleId = "digital_storage.module_stack_display";

    /// <summary>The module type only terminal module bays accept.</summary>
    public const string TerminalModuleType = "digital_storage.terminal_module";

    /// <summary>The terminal tag holding the stacking settings.</summary>
    public const string ConfigTag = "digital_storage.terminal_stacking";

    /// <summary>Whether the terminal of a context has the module installed.</summary>
    public static bool IsInstalled(TerminalContext context) => context.InstalledModules.Contains(ModuleId);
}
