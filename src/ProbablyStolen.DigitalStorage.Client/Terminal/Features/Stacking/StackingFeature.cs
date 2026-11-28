using System;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
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

    /// <summary>The delete button id, shown for one merged slot; it deletes only the item rows merging that slot.</summary>
    public const string DeleteActionId = "stacking.delete";

    /// <summary>The global add button id, shown for two or more slots of one item that differ in fields no global row names.</summary>
    public const string GlobalAddActionId = "stacking.global_add";

    /// <summary>The global delete button id, shown for one merged slot; it deletes only the global rows merging that slot.</summary>
    public const string GlobalDeleteActionId = "stacking.global_delete";

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
    /// <param name="identities">Tells why an entry's identity could not be read, for the selection tooltip.</param>
    public static void Register(TerminalRegistry registry, IStackingPlatform platform, Action<string> diagnostic,
        Func<bool> diagnosticsEnabled, IItemIdentityDiagnostics identities)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (platform == null) throw new ArgumentNullException(nameof(platform));
        if (identities == null) throw new ArgumentNullException(nameof(identities));
        var storage = ActionScope.View(TerminalRegistry.StorageTerminalName);
        var rules = new ButtonAppearance("button_stack", "terminal_button_stacking_tip");
        // After the sort order (0) and sort direction (10) buttons of the storage view.
        registry.AddAction(new TerminalAction(ActionId, 20, DockSide.Left, ActionPlacement.Rail, storage,
            StackingModule.IsInstalled, _ => rules,
            context => context.Commands.PushScreen(platform.CreateRulesScreen(context.Window))));

        AddQuick(registry, AddActionId, 0, (selection, _) => selection.Kind == StackingQuickKind.AddNew, "button_add",
            "stacking_quick_add_tip", context => QuickAdd(context, platform));
        AddQuick(registry, AppendActionId, 0, (selection, _) => selection.Kind == StackingQuickKind.AddToExisting, "button_add",
            "stacking_quick_append_tip", context => QuickAdd(context, platform));
        var globalAdd = new GlobalAddCheck(platform);
        AddQuick(registry, GlobalAddActionId, 5, globalAdd.IsAvailable, GlobalAddIcon, "stacking_quick_global_add_tip",
            context => QuickAddGlobal(context, platform));
        AddQuick(registry, EditActionId, 10, (selection, _) => selection.Kind == StackingQuickKind.EditMixed, "button_edit",
            "stacking_quick_edit_tip",
            context =>
            {
                var selection = Classify(context);
                if (selection.Kind == StackingQuickKind.EditMixed)
                    context.Commands.PushScreen(platform.CreateItemEditor(context.Window, selection.ItemId!));
                else context.Commands.ClearSelection();
            });
        AddQuick(registry, DeleteActionId, 20, (selection, _) => selection.Kind == StackingQuickKind.EditMixed &&
            selection.HasItemRule, "button_delete", "stacking_quick_delete_tip",
            context => QuickDelete(context, platform, StackingQuickActions.DeleteForSelection,
                StackingQuickActions.DeletedKey));
        AddQuick(registry, GlobalDeleteActionId, 25, (selection, context) => selection.Kind == StackingQuickKind.EditMixed &&
            StackingQuickActions.HasGlobalIgnore(Config(context)), GlobalDeleteIcon,
            "stacking_quick_global_delete_tip",
            context => QuickDelete(context, platform, StackingQuickActions.DeleteGlobalForSelection,
                StackingQuickActions.GlobalDeletedKey));

        ScreenBackButton.Register(registry, RulesScreenId, StorageTerminal.StorageTerminalFeature.RightDockTitleKey);
        ScreenBackButton.Register(registry, ItemEditorScreenId, StorageTerminal.StorageTerminalFeature.RightDockTitleKey);

        registry.AddStorageProjector(new StackingProjector(diagnostic, diagnosticsEnabled));
        registry.AddSlotDecorator(new MixedGroupDecorator());
        registry.AddTooltipPanel(StackingInfoTooltip.Panel);
        registry.AddTooltipContributor(new StackingInfoTooltip(identities));
    }

    /// <summary>The icon of the global add button.</summary>
    public const string GlobalAddIcon = "button_add_global";

    /// <summary>The icon of the global delete button.</summary>
    public const string GlobalDeleteIcon = "button_delete_global";

    private static void AddQuick(TerminalRegistry registry, string id, int order,
        Func<StackingQuickSelection, TerminalContext, bool> shows, string icon, string tooltip, Action<TerminalContext> press)
    {
        var appearance = new ButtonAppearance(icon, tooltip);
        registry.AddAction(new TerminalAction(id, order, DockSide.Right, ActionPlacement.Rail,
            ActionScope.View(TerminalRegistry.StorageTerminalName), context => shows(Classify(context), context),
            _ => appearance, press));
    }

    private static StackingConfig? Config(TerminalContext context) => StackingProjector.StoreOf(context.FeatureData).Effective();

    private static StackingQuickSelection Classify(TerminalContext context) =>
        StackingQuickActions.Classify(context, StackingProjector.StoreOf(context.FeatureData).Effective());

    private static void QuickDelete(TerminalContext context, IStackingPlatform platform,
        Func<TerminalContext, StackingConfigStore, Extensions.EntryAttributes, string?> delete, string deletedKey)
    {
        var selection = Classify(context);
        var message = selection.Kind != StackingQuickKind.EditMixed ? null
            : delete(context, StackingProjector.StoreOf(context.FeatureData),
                platform.ReadAttributes(context.Window, selection.Groups[0].Members));
        if (message == deletedKey) platform.Regroup(context.Window);
        context.Commands.ClearSelection();
        if (message != null) context.Commands.ShowMessage(message);
    }

    private static void QuickAddGlobal(TerminalContext context, IStackingPlatform platform)
    {
        var selection = Classify(context);
        var result = StackingQuickResult.Unavailable;
        if (selection.Kind is StackingQuickKind.AddNew or StackingQuickKind.AddToExisting)
        {
            var members = selection.Groups.Select(group => group.Representative).ToArray();
            result = StackingQuickActions.AddGlobalFromSelection(context, StackingProjector.StoreOf(context.FeatureData),
                platform.ReadAttributes(context.Window, members));
        }

        if (result is StackingQuickResult.Merged or StackingQuickResult.StillSeparate or StackingQuickResult.OverriddenByItem)
            platform.Regroup(context.Window);
        context.Commands.ClearSelection();
        if (StackingQuickActions.MessageOf(result) is { } message) context.Commands.ShowMessage(message);
    }

    /// <summary>
    /// Decides whether the global add shows. It compares the selected slots, which reads their attributes, so the
    /// answer is kept until the selection or the settings change.
    /// </summary>
    private sealed class GlobalAddCheck
    {
        private readonly IStackingPlatform _platform;
        private StackingConfig? _config;
        private string[]? _groups;
        private bool _available;

        internal GlobalAddCheck(IStackingPlatform platform) => _platform = platform;

        internal bool IsAvailable(StackingQuickSelection selection, TerminalContext context)
        {
            if (selection.Kind is not (StackingQuickKind.AddNew or StackingQuickKind.AddToExisting)) return false;
            var config = Config(context);
            if (config == null) return false;
            var groups = selection.Groups.Select(group => group.Id).ToArray();
            if (ReferenceEquals(config, _config) && _groups != null && _groups.SequenceEqual(groups)) return _available;
            var members = selection.Groups.Select(group => group.Representative).ToArray();
            _available = StackingQuickActions.GlobalAdditions(selection, config,
                _platform.ReadAttributes(context.Window, members)).Count > 0;
            _config = config;
            _groups = groups;
            return _available;
        }
    }

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
