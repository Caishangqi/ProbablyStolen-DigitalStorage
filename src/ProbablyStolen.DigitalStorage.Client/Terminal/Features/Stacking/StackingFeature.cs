using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// The stack display feature: a left dock button that opens the rules page, the quick rule actions on the right dock,
/// the grouping projector, the mixed slot mark and the merged slot tooltip panel. Everything stays inactive until the
/// module is installed in the terminal.
/// </summary>
public static class StackingFeature
{
    /// <summary>The rules page button id.</summary>
    public const string ActionId = "stacking";

    /// <summary>The quick add button id, shown for two or more slots of an item without a rule.</summary>
    public const string AddActionId = "stacking.add";

    /// <summary>The quick add button id, shown for two or more slots of an item that has a rule, merged slots included.</summary>
    public const string AppendActionId = "stacking.append";

    /// <summary>The edit button id, shown for one merged slot.</summary>
    public const string EditActionId = "stacking.edit";

    /// <summary>The delete button id, shown for one merged slot; it deletes only the rows merging that slot.</summary>
    public const string DeleteActionId = "stacking.delete";

    /// <summary>The screen id of the rules page.</summary>
    public const string RulesScreenId = "stacking.rules";

    /// <summary>The screen id of one item's editor page.</summary>
    public const string ItemEditorScreenId = "stacking.item_editor";

    /// <summary>The back button id of the rules page.</summary>
    public const string RulesBackActionId = RulesScreenId + ScreenBackButton.IdSuffix;

    /// <summary>The back button id of the editor page.</summary>
    public const string ItemEditorBackActionId = ItemEditorScreenId + ScreenBackButton.IdSuffix;

    /// <summary>Registers the buttons and the storage extensions.</summary>
    /// <param name="registry">The registry; the storage view's right dock must be registered by freeze.</param>
    /// <param name="platform">Creates pages and reads values in the platform window.</param>
    /// <param name="diagnostic">Receives grouping diagnostics.</param>
    /// <param name="diagnosticsEnabled">Whether to explain why entries of one item stay apart.</param>
    public static void Register(TerminalRegistry registry, IStackingPlatform platform, Action<string> diagnostic,
        Func<bool> diagnosticsEnabled)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (platform == null) throw new ArgumentNullException(nameof(platform));
        var storage = ActionScope.View(TerminalRegistry.StorageTerminalName);
        var rules = new ButtonAppearance("button_stack", "terminal_button_stacking_tip");
        // After the sort order (0) and sort direction (10) buttons of the storage view.
        registry.AddAction(new TerminalAction(ActionId, 20, DockSide.Left, ActionPlacement.Rail, storage,
            StackingModule.IsInstalled, _ => rules,
            context => context.Commands.PushScreen(platform.CreateRulesScreen(context.Window))));

        AddQuick(registry, AddActionId, 0, StackingQuickKind.AddNew, "button_add", "stacking_quick_add_tip",
            context => QuickAdd(context, platform));
        AddQuick(registry, AppendActionId, 0, StackingQuickKind.AddToExisting, "button_add", "stacking_quick_append_tip",
            context => QuickAdd(context, platform));
        AddQuick(registry, EditActionId, 10, StackingQuickKind.EditMixed, "button_edit", "stacking_quick_edit_tip",
            context =>
            {
                var selection = Classify(context);
                if (selection.Kind == StackingQuickKind.EditMixed)
                    context.Commands.PushScreen(platform.CreateItemEditor(context.Window, selection.ItemId!));
                else context.Commands.ClearSelection();
            });
        AddQuick(registry, DeleteActionId, 20, StackingQuickKind.EditMixed, "button_delete", "stacking_quick_delete_tip",
            context =>
            {
                var selection = Classify(context);
                var message = selection.Kind != StackingQuickKind.EditMixed ? null
                    : StackingQuickActions.DeleteForSelection(context, StackingProjector.StoreOf(context.FeatureData),
                        platform.ReadAttributes(context.Window, selection.Groups[0].Members));
                if (message == StackingQuickActions.DeletedKey) platform.Regroup(context.Window);
                context.Commands.ClearSelection();
                if (message != null) context.Commands.ShowMessage(message);
            });

        ScreenBackButton.Register(registry, RulesScreenId, StorageTerminal.StorageTerminalFeature.RightDockTitleKey);
        ScreenBackButton.Register(registry, ItemEditorScreenId, StorageTerminal.StorageTerminalFeature.RightDockTitleKey);

        registry.AddStorageProjector(new StackingProjector(diagnostic, diagnosticsEnabled));
        registry.AddSlotDecorator(new MixedGroupDecorator());
        registry.AddTooltipPanel(StackingInfoTooltip.Panel);
        registry.AddTooltipContributor(new StackingInfoTooltip());
    }

    private static void AddQuick(TerminalRegistry registry, string id, int order, StackingQuickKind kind, string icon,
        string tooltip, Action<TerminalContext> press)
    {
        var appearance = new ButtonAppearance(icon, tooltip);
        registry.AddAction(new TerminalAction(id, order, DockSide.Right, ActionPlacement.Rail,
            ActionScope.View(TerminalRegistry.StorageTerminalName), context => Classify(context).Kind == kind,
            _ => appearance, press));
    }

    private static StackingQuickSelection Classify(TerminalContext context) =>
        StackingQuickActions.Classify(context, StackingProjector.StoreOf(context.FeatureData).Effective());

    private static void QuickAdd(TerminalContext context, IStackingPlatform platform)
    {
        var selection = Classify(context);
        var result = StackingQuickResult.Unavailable;
        if (selection.Kind is StackingQuickKind.AddNew or StackingQuickKind.AddToExisting)
        {
            var members = selection.Groups.Select(group => group.Representative).ToArray();
            result = StackingQuickActions.AddFromSelection(context, StackingProjector.StoreOf(context.FeatureData),
                platform.ReadAttributes(context.Window, members));
        }

        if (result is StackingQuickResult.Merged or StackingQuickResult.StillSeparate or StackingQuickResult.RuleOff)
            platform.Regroup(context.Window);
        context.Commands.ClearSelection();
        if (StackingQuickActions.MessageOf(result) is { } message) context.Commands.ShowMessage(message);
    }
}
