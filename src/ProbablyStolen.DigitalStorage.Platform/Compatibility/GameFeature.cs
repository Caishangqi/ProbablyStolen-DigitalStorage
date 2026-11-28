namespace ProbablyStolen.DigitalStorage.Platform.Compatibility;

/// <summary>The feature each patch group serves; one group per feature.</summary>
public enum GameFeature
{
    /// <summary>Rebuilds storage cell pages while a save decodes.</summary>
    CellPageRestore,

    /// <summary>Keeps stored items out of the store's item scan.</summary>
    StoredPresence,

    /// <summary>Keeps liquids in storage from cooling and reacting.</summary>
    StoredLiquid,

    /// <summary>Notices changes other code makes to stored items and undoes those made while this mod reads them.</summary>
    StoredItemChanges,

    /// <summary>Module bay expansion stages on storage machines.</summary>
    ModuleBayExpansion,

    /// <summary>Opens the terminal's storage window.</summary>
    TerminalOpen,

    /// <summary>Stores a single dragged item dropped onto a terminal.</summary>
    TerminalSingleDrop,

    /// <summary>Stores a selected group dropped onto a terminal.</summary>
    TerminalGroupDrop,

    /// <summary>Closes the partition editor with its window or cell; the editor is offered only while it is active.</summary>
    PartitionEditorClose,

    /// <summary>Keeps the wheel over a sample window from changing the held quantity.</summary>
    TerminalSampleWheel,

    /// <summary>Spreads a group dropped onto one bay over its sibling bays.</summary>
    MachineBayGroupDrop,

    /// <summary>Prefers the first free bay among ties in registered machine grids.</summary>
    ForwardFill,

    /// <summary>Box selection over native panels shown in mod windows.</summary>
    BorrowedPanelBand,

    /// <summary>The back key closes mod windows in focus order.</summary>
    WindowBack,

    /// <summary>Forgets remembered window positions of destroyed items.</summary>
    WindowPositionForget,

    /// <summary>Network power lines in item tooltips.</summary>
    NetworkPowerTooltip,

    /// <summary>Effective values in type-limit module tooltips.</summary>
    TypeLimitTooltip,

    /// <summary>Module highlight in the workbench while a module is pressed.</summary>
    WorkbenchModuleHighlight,

    /// <summary>Refreshes mod text after a language change.</summary>
    LocaleRefresh,

    /// <summary>Localizes the settings window and its integration switches.</summary>
    ModConfigWindow
}
