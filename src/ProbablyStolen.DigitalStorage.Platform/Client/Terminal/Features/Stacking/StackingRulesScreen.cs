using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Stacking;

/// <summary>
/// The stacking rules page: the merged display switch, the mark color, one card per rule with field and mode lists,
/// adding with the searchable attribute menu, and clearing. It only draws; every decision and save goes through the
/// editor.
/// </summary>
internal sealed class StackingRulesScreen : SubScreen
{
    private const float Gap = 4f;
    private const float FieldWidth = 180f;
    private const float ModeWidth = 96f;
    private const float ColorInputWidth = 84f;
    private const float CountWidth = 110f;
    private const int CardsBeforeScroll = 4;
    private static readonly StackingMode[] Modes = { StackingMode.Match, StackingMode.Ignore };

    private readonly StackingRulesEditor _editor;
    private readonly StorageTerminalServices _storage;
    private PopupDropdown? _dropdown;
    private TerminalInventoryRefreshHandler? _frame;

    internal StackingRulesScreen(TerminalWindowHost window, StorageTerminalServices storage) : base(window)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        var data = window.CurrentContext?.FeatureData ?? throw new InvalidOperationException("The window is not open.");
        _editor = new StackingRulesEditor(StackingProjector.StoreOf(data), window.StoragePipeline.Invalidate);
    }

    internal override bool FitsContent => true;

    private static float BodyWidth => TerminalWindowMetrics.GridWidth;

    protected override bool BuildContent(CustomUIBuilder builder)
    {
        var text = Window.Text;
        _dropdown ??= new PopupDropdown(Window.CustomWindow, _storage.Search.MatchesText);
        if (_frame == null)
        {
            _frame = Window.CustomWindow.root.AddComponent<TerminalInventoryRefreshHandler>();
            _frame.Initialize(() => _dropdown?.CloseOnOutsidePress(EventCamera()));
        }

        var config = _editor.Config;
        builder.AddToggle(text.Get("stacking_enable"), config.Enabled, enabled => Edit(() => _editor.SetEnabled(enabled)));
        builder.AddLabel(text.Get("stacking_scope_hint"));
        Muted(builder.Last);
        if (!_editor.IsEditable)
        {
            builder.AddWarning(text.Get("stacking_config_unreadable"));
            return true;
        }

        BuildMarker(builder, config);
        BuildListHeader(builder, config);
        BuildCards(builder, config);
        BuildButtons(builder, config);
        BuildSummary(builder, config);
        builder.AddLabel(text.Get(_editor.LastError ?? "stacking_saved_immediately"));
        Muted(builder.Last);
        return true;
    }

    public override bool HandleBack()
    {
        if (_dropdown?.IsOpen != true) return false;
        _dropdown.Close();
        return true;
    }

    internal override void Dispose()
    {
        _dropdown?.Close();
        _dropdown = null;
        if (_frame != null) UnityEngine.Object.Destroy(_frame);
        _frame = null;
        base.Dispose();
    }

    /// <summary>The mark color: a #RGB or #RRGGBB input, a preview of the tinted mark, and preset swatches.</summary>
    private void BuildMarker(CustomUIBuilder builder, StackingConfig config)
    {
        var text = Window.Text;
        builder.AddLabel(text.Get("stacking_marker_color"));
        builder.BeginRow(Gap, TextAnchor.MiddleLeft);
        RuleCardWidgets.FixWidth(builder.CurrentContainer.gameObject, BodyWidth);
        builder.AddInput(text.Get("stacking_marker_placeholder"), config.Marker.Format(),
            value => Edit(() => _editor.SetMarkerColor(value)));
        RuleCardWidgets.SetSize(builder.Last, ColorInputWidth, RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetText(builder.Last, text.Get("stacking_marker_color_tip"));
        var overlay = Window.ButtonIcons.Overlay(MixedGroupDecorator.OverlayKey);
        if (overlay != null)
        {
            builder.AddImage(overlay, RuleCardWidgets.ControlHeight, RuleCardWidgets.ControlHeight);
            if (builder.Last.image != null) builder.Last.image.color = Tint(config.Marker);
        }

        foreach (var preset in StackingMarker.Presets)
        {
            var color = preset;
            builder.AddPointerButton(string.Empty, () => Edit(() => _editor.SetMarkerColor(color.Format())), null);
            RuleCardWidgets.SetSize(builder.Last, RuleCardWidgets.ControlHeight, RuleCardWidgets.ControlHeight);
            if (builder.Last.image != null)
            {
                builder.Last.image.sprite = null;
                builder.Last.image.color = Tint(color);
            }

            TerminalTooltipPresenter.SetButtonText(builder.Last, color.Format());
        }

        builder.End();
    }

    private static Color Tint(StackingMarker marker) =>
        RenderHandler.GetColorFromPalette((RenderHandler.ColorPalette)marker.Rgb);

    /// <summary>The list title with the rule count on the right, as on the partition page.</summary>
    private void BuildListHeader(CustomUIBuilder builder, StackingConfig config)
    {
        var text = Window.Text;
        builder.BeginRow(Gap);
        builder.AddLabel(text.Get("stacking_conditions"));
        RuleCardWidgets.FixWidth(builder.Last.root, BodyWidth - Gap - CountWidth);
        builder.AddLabel(text.Get("stacking_rule_count", config.Rules.Count));
        RuleCardWidgets.FixWidth(builder.Last.root, CountWidth);
        if (builder.Last.text != null)
        {
            builder.Last.text.alignment = TMPro.TextAlignmentOptions.TopRight;
            builder.Last.text.color = MachineryStyle.BUTTON_TEXT_DISABLED;
        }

        builder.End();
    }

    /// <summary>The rule cards inside a light box that stays visible, one card tall, while there are no rules.</summary>
    private void BuildCards(CustomUIBuilder builder, StackingConfig config)
    {
        RuleCardWidgets.BeginListBox(builder, Gap, BodyWidth);
        var inner = BodyWidth - 2 * RuleCardWidgets.ListPadding;
        var cardWidth = inner;
        if (config.Rules.Count > CardsBeforeScroll)
        {
            var parent = builder.CurrentContainer;
            var cardHeight = RuleCardWidgets.ControlHeight + 2 * RuleCardWidgets.CardPadding;
            builder.BeginScroll(CardsBeforeScroll * (cardHeight + Gap) - Gap);
            var view = parent.GetChild(parent.childCount - 1).gameObject;
            RuleCardWidgets.FixWidth(view, inner);
            cardWidth = inner - RuleCardWidgets.ScrollbarSpace(view);
        }
        else builder.BeginColumn(Gap);

        for (var row = 0; row < config.Rules.Count; row++) BuildCard(builder, config, row, cardWidth);
        builder.End();
        builder.End();
    }

    private void BuildCard(CustomUIBuilder builder, StackingConfig config, int row, float width)
    {
        var text = Window.Text;
        var rule = config.Rules[row];
        RuleCardWidgets.BeginCard(builder, Gap, width);
        CustomUIElement? field = null;
        field = PopupDropdown.AddField(builder, FieldName(rule.FieldId), FieldWidth, () => OpenFieldPicker(field!, row));
        TerminalTooltipPresenter.SetButtonText(field, FieldName(rule.FieldId));
        var modeLabels = Modes.Select(mode => new DropdownOption(text.Get(ModeKey(mode)))).ToList();
        var current = Array.IndexOf(Modes, rule.Mode);
        CustomUIElement? mode = null;
        mode = PopupDropdown.AddField(builder, modeLabels[current].Label, ModeWidth, () =>
            Toggle(mode!, modeLabels, current, choice => Edit(() => _editor.SetMode(row, Modes[choice]))));
        builder.AddFlexibleSpace();
        var delete = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, "button_delete", false,
            () => Edit(() => _editor.Remove(row)), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(delete, text.Get("stacking_remove_rule_tip"));
        builder.End();
        foreach (var note in StackingRuleSummary.RowNotes(config, row))
        {
            builder.AddLabel(text.Get(note));
            Muted(builder.Last);
            RuleCardWidgets.FixWidth(builder.Last.root, width);
        }
    }

    private void BuildButtons(CustomUIBuilder builder, StackingConfig config)
    {
        var text = Window.Text;
        builder.BeginRow(Gap, TextAnchor.MiddleLeft);
        RuleCardWidgets.FixWidth(builder.CurrentContainer.gameObject, BodyWidth);
        CustomUIElement? add = null;
        add = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, ButtonIcons.Add, false,
            () => OpenFieldPicker(add!, -1), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(add, text.Get("stacking_add_rule"));
        builder.AddFlexibleSpace();
        var clear = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, "button_clear", false,
            () => Edit(() => _editor.Clear()), RuleCardWidgets.ControlHeight);
        if (clear.button != null) clear.button.interactable = config.Rules.Count > 0;
        TerminalTooltipPresenter.SetButtonText(clear, text.Get("stacking_clear_rules"));
        builder.End();
    }

    /// <summary>The final meaning of the rules in the same framed box the partition page uses.</summary>
    private void BuildSummary(CustomUIBuilder builder, StackingConfig config)
    {
        var (key, ignored) = StackingRuleSummary.Describe(config);
        var names = string.Join(Window.Text.Get("stacking_summary_separator"), ignored.Select(FieldName));
        RuleCardWidgets.BeginSpecBox(builder);
        builder.AddLabel(Window.Text.Get(key, names));
        if (builder.Last.text != null)
        {
            builder.Last.text.enableWordWrapping = true;
            builder.Last.text.color = MachineryStyle.VALUE_TEXT;
        }

        RuleCardWidgets.FixWidth(builder.Last.root, BodyWidth - 20f);
        builder.End();
    }

    /// <summary>Opens the attribute menu for a row, or for a new row when <paramref name="row"/> is -1.</summary>
    private void OpenFieldPicker(CustomUIElement anchor, int row)
    {
        var options = _editor.MenuOptions(StoredKeys());
        var labels = options.Select(option => new DropdownOption(FieldName(option.Field.Id),
            option.AbsentFromInventory ? Window.Text.Get("stacking_field_absent") : DescriptionOf(option.Field))).ToList();
        var config = _editor.Config;
        var current = row < 0 ? -1 : options.ToList().FindIndex(option => option.Field.Id == config.Rules[row].FieldId);
        Toggle(anchor, labels, current, choice =>
        {
            var id = options[choice].Field.Id;
            Edit(() => (row < 0 ? _editor.AddOrLocate(id) : _editor.ReplaceField(row, id)) >= 0);
        }, Window.Text.Get("stacking_search"));
    }

    private void Toggle(CustomUIElement field, IReadOnlyList<DropdownOption> options, int selected, Action<int> choose,
        string? searchPlaceholder = null)
    {
        if (_dropdown == null || !((ITerminalViewHost)Window).IsCurrentSession()) return;
        if (_dropdown.IsOpenFor(field)) _dropdown.Close();
        else _dropdown.Open(field, options, selected, choose, searchPlaceholder);
    }

    private void Edit(Func<bool> edit)
    {
        _dropdown?.Close();
        edit();
        RefreshContent();
        // Rows, notes and the summary change the page height, so the window is measured again.
        Window.FitSubScreenContent(this);
    }

    private IEnumerable<ItemKey> StoredKeys()
    {
        try
        {
            return _storage.ReadSnapshot().Entries.Select(entry => entry.Key).ToArray();
        }
        catch (Exception error)
        {
            Window.WriteDiagnostic($"stacking_page failed=read_inventory error={error.GetType().Name}");
            return Array.Empty<ItemKey>();
        }
    }

    private string FieldName(string fieldId)
    {
        var field = StackingFieldCatalog.Find(fieldId);
        if (field == null) return fieldId;
        return field.FeatureId != null ? Window.Text.Get(field.NameKey, field.FeatureId) : Window.Text.Get(field.NameKey);
    }

    private string DescriptionOf(StackingField field) =>
        field.FeatureId != null ? Window.Text.Get(field.DescriptionKey, field.FeatureId) : Window.Text.Get(field.DescriptionKey);

    private static string ModeKey(StackingMode mode) => mode == StackingMode.Ignore ? "stacking_mode_ignore" : "stacking_mode_match";

    private static void Muted(CustomUIElement label)
    {
        if (label.text != null) label.text.color = MachineryStyle.BUTTON_TEXT_DISABLED;
    }

    private Camera? EventCamera()
    {
        var canvas = Window.CustomWindow.Rect.GetComponentInParent<Canvas>();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }
}
