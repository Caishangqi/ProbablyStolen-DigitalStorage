using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Drive;

/// <summary>
/// The drive window's registry: the cell bay view plus left dock buttons for the module page and the priority page,
/// whose back buttons sit on the left dock titled "Actions". Built through the same registration as the storage
/// terminal, so the dock and sub-pages behave alike.
/// </summary>
public static class DriveTerminalFeature
{
    /// <summary>The name of the cell bay view.</summary>
    public const string TerminalName = "drive";

    /// <summary>The id of the priority button.</summary>
    public const string PriorityActionId = "drive_priority";

    /// <summary>The screen id of the priority page.</summary>
    public const string PriorityScreenId = "drive.priority";

    /// <summary>The id of the priority page's back button.</summary>
    public const string PriorityBackActionId = PriorityScreenId + ScreenBackButton.IdSuffix;

    /// <summary>The id of the module page's back button.</summary>
    public const string ModulesBackActionId = ModulesFeature.ScreenId + ScreenBackButton.IdSuffix;

    /// <summary>Creates and freezes the registry of one drive window.</summary>
    /// <param name="config">Stores registered setting values.</param>
    /// <param name="diagnostic">Receives refused registrations.</param>
    /// <param name="createView">Creates the cell bay view for a window.</param>
    /// <param name="createModulesScreen">Creates the module page for a window.</param>
    /// <param name="createPriorityScreen">Creates the priority page for a window.</param>
    public static TerminalRegistry CreateRegistry(IConfigManager config, Action<string> diagnostic,
        Func<object, object> createView, Func<object, ITerminalScreen> createModulesScreen,
        Func<object, ITerminalScreen> createPriorityScreen)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        if (createView == null) throw new ArgumentNullException(nameof(createView));
        if (createPriorityScreen == null) throw new ArgumentNullException(nameof(createPriorityScreen));

        var registry = new TerminalRegistry(config, diagnostic, TerminalName);
        registry.AddDock(DockSide.Left, StorageTerminalFeature.LeftDockTitleKey);
        registry.AddTerminal(new TerminalDefinition(TerminalName, 0, "drive_name", _ => true, createView));
        ModulesFeature.Register(registry, createModulesScreen);
        var priority = new ButtonAppearance("button_priority", "priority_configure");
        registry.AddAction(new TerminalAction(PriorityActionId, 1, DockSide.Left, ActionPlacement.Rail,
            ActionScope.AllViews, _ => true, _ => priority,
            context => context.Commands.PushScreen(createPriorityScreen(context.Window))));
        ScreenBackButton.Register(registry, ModulesFeature.ScreenId, StorageTerminalFeature.RightDockTitleKey);
        ScreenBackButton.Register(registry, PriorityScreenId, StorageTerminalFeature.RightDockTitleKey);
        registry.Freeze();
        return registry;
    }
}
