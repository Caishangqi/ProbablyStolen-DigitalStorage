using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Tooltips;
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
    private const float FieldWidth = 224f;
    private const float FieldPickerWidth = 320f;
    private const float ModeWidth = 96f;
    private const float ColorInputWidth = 84f;
    private const float CountWidth = 110f;
    private const int CardsBeforeScroll = 4;
    private const float CardHeight = RuleCardWidgets.ControlHeight + 2 * RuleCardWidgets.CardPadding;
    private static readonly Color LocatedCardFill = new(1f, 1f, 1f, 0.16f);
    private static readonly StackingMode[] Modes = { StackingMode.Match, StackingMode.Ignore };

    private readonly StackingRulesEditor _editor;
    private readonly StorageTerminalServices _storage;
    private PopupDropdown? _dropdown;
    private TerminalInventoryRefreshHandler? _frame;
    private RectTransform? _cardHost;
    private ScrollRect? _ruleScroll;
    private int? _revealedPosition;
    private string? _revealedFieldId;
    private string _ruleQuery = string.Empty;

    internal StackingRulesScreen(TerminalWindowHost window, StorageTerminalServices storage) : base(window)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        var data = window.CurrentContext?.FeatureData ?? throw new InvalidOperationException("The window is not open.");
        _editor = new StackingRulesEditor(StackingProjector.StoreOf(data), window.StoragePipeline.Invalidate);
    }

    internal override bool FitsContent => true;

    private static float BodyWidth => TerminalWindowMetrics.GridWidth + 36f;

    protected override bool BuildContent(CustomUIBuilder builder)
    {
        var text = Window.Text;
        _dropdown ??= new PopupDropdown(Window.CustomWindow, _storage.Search.MatchesText);
        if (_frame == null)
        {
            _frame = Window.CustomWindow.root.AddComponent<TerminalInventoryRefreshHandler>();
            _frame.Initialize(() => _dropdown?.CloseOnOutsidePress(EventCamera()));
        }

        _cardHost = null;
        _ruleScroll = null;
        var config = _editor.Config;
        builder.AddToggle(text.Get("stacking_enable"), config.Enabled, enabled => Edit(() => _editor.SetEnabled(enabled)));
        builder.AddLabel(text.Get("stacking_scope_hint"));
        Muted(builder.Last);
        if (!_editor.IsEditable)
        {
            builder.AddWarning(text.Get("stacking_config_unreadable"));
            return true;
        }

        if (_editor.WasReset) builder.AddWarning(text.Get("stacking_rules_reset"));
        BuildMarker(builder, config);
        BuildListHeader(builder, config);
        builder.AddInput(text.Get("stacking_rule_search"), _ruleQuery, onTyping: typed =>
        {
            _ruleQuery = typed ?? string.Empty;
            _revealedFieldId = null;
            RebuildCards();
            Window.FitSubScreenContent(this);
        });
        RuleCardWidgets.SetSize(builder.Last, BodyWidth, RuleCardWidgets.ControlHeight);
        builder.BeginColumn(Gap);
        _cardHost = builder.CurrentContainer;
        RebuildCards();
        builder.End();
        BuildButtons(builder, config);
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
        _cardHost = null;
        _ruleScroll = null;
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

    private void RebuildCards()
    {
        if (_cardHost == null) return;
        _dropdown?.Close();
        CustomUIBuilder.ClearContainer(_cardHost);
        _ruleScroll = null;
        _revealedPosition = null;
        var config = _editor.Config;
        var rows = StackingRulesEditor.FindRows(config, _ruleQuery, FieldName, _storage.Search.MatchesText, _revealedFieldId);
        var builder = new CustomUIBuilder(CustomUIManager.Instance, Window.CustomWindow, _cardHost);
        RuleCardWidgets.BeginListBox(builder, Gap, BodyWidth, RuleCardWidgets.CardFill);
        var inner = BodyWidth - 2 * RuleCardWidgets.ListPadding;
        var cardWidth = inner;
        if (rows.Count > CardsBeforeScroll)
        {
            var parent = builder.CurrentContainer;
            builder.BeginScroll(CardsBeforeScroll * (CardHeight + Gap) - Gap);
            var view = parent.GetChild(parent.childCount - 1).gameObject;
            cardWidth = RuleCardWidgets.ConfigureListScroll(view, inner, Gap);
            _ruleScroll = view.GetComponentInChildren<ScrollRect>(true);
        }
        else builder.BeginColumn(Gap);

        for (var position = 0; position < rows.Count; position++)
        {
            var row = rows[position];
            var located = config.Rules[row].FieldId == _revealedFieldId;
            if (located) _revealedPosition = position;
            BuildCard(builder, config, row, cardWidth, located);
        }
        if (rows.Count == 0 && _ruleQuery.Trim().Length != 0)
        {
            builder.AddLabel(Window.Text.Get("stacking_rule_search_empty"));
            Muted(builder.Last);
        }
        builder.End();
        builder.End();
    }

    private void BuildCard(CustomUIBuilder builder, StackingConfig config, int row, float width, bool located)
    {
        var text = Window.Text;
        var rule = config.Rules[row];
        RuleCardWidgets.BeginCard(builder, Gap, width);
        if (located) RuleCardWidgets.Fill(builder.CurrentContainer.gameObject, LocatedCardFill);
        var fieldName = TooltipLiteralText.ForRichText(FieldName(rule.FieldId));
        CustomUIElement? field = null;
        field = PopupDropdown.AddField(builder, fieldName, FieldWidth, () => OpenFieldPicker(field!, row));
        var description = StackingFieldCatalog.Find(rule.FieldId) is { } attribute ? DescriptionOf(attribute) : string.Empty;
        var lore = new[] { fieldName, TooltipLiteralText.ForRichText(description) }
            .Concat(StackingRuleSummary.RowNotes(config, row).Select(note => text.Get(note)))
            .Where(line => !string.IsNullOrEmpty(line));
        TerminalTooltipPresenter.SetButtonText(field, string.Join("\n", lore));
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

    /// <summary>Opens the attribute menu for a row, or for a new row when <paramref name="row"/> is -1.</summary>
    private void OpenFieldPicker(CustomUIElement anchor, int row)
    {
        if (_dropdown == null || !((ITerminalViewHost)Window).IsCurrentSession()) return;
        if (_dropdown.IsOpenFor(anchor))
        {
            _dropdown.Close();
            return;
        }

        var options = _editor.MenuOptions(StoredKeys());
        var absence = Window.Text.Get("stacking_field_absent");
        var names = options.Select(option => FieldName(option.Field.Id)).ToArray();
        var details = options.Select((option, index) => (Title: TooltipLiteralText.ForRichText(names[index]),
            Description: TooltipLiteralText.ForRichText(DescriptionOf(option.Field)),
            Absence: option.AbsentFromInventory ? absence : null)).ToArray();
        var labels = details.Select((detail, index) =>
            new DropdownOption(detail.Title, detail.Absence ?? detail.Description, names[index])).ToArray();
        var config = _editor.Config;
        var current = row < 0 ? -1 : options.ToList().FindIndex(option => option.Field.Id == config.Rules[row].FieldId);
        _dropdown.Open(anchor, labels, current, choice =>
        {
            var id = options[choice].Field.Id;
            Edit(() =>
            {
                var selected = row < 0 ? _editor.AddOrLocate(id) : _editor.ReplaceField(row, id);
                if (selected < 0) return false;
                _revealedFieldId = _editor.Config.Rules[selected].FieldId;
                return true;
            }, reveal: true);
        }, Window.Text.Get("stacking_search"), preferredWidth: FieldPickerWidth, hover: new StackingFieldTooltip(details));
    }

    private void Toggle(CustomUIElement field, IReadOnlyList<DropdownOption> options, int selected, Action<int> choose)
    {
        if (_dropdown == null || !((ITerminalViewHost)Window).IsCurrentSession()) return;
        if (_dropdown.IsOpenFor(field)) _dropdown.Close();
        else _dropdown.Open(field, options, selected, choose);
    }

    private void Edit(Func<bool> edit, bool reveal = false)
    {
        _dropdown?.Close();
        var offset = _ruleScroll?.content?.anchoredPosition.y ?? 0f;
        var changed = edit();
        RefreshContent();
        Window.FitSubScreenContent(this);
        RestoreRuleScroll(offset, reveal && changed);
    }

    private void RestoreRuleScroll(float offset, bool reveal)
    {
        if (_ruleScroll == null || _ruleScroll.content == null || _ruleScroll.viewport == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_ruleScroll.transform);
        _ruleScroll.StopMovement();
        var viewportHeight = _ruleScroll.viewport.rect.height;
        var maxOffset = Mathf.Max(0f, _ruleScroll.content.rect.height - viewportHeight);
        if (reveal && _revealedPosition is { } position)
        {
            var rowTop = position * (CardHeight + Gap);
            var rowBottom = rowTop + CardHeight;
            if (offset > rowTop) offset = rowTop;
            else if (offset + viewportHeight < rowBottom) offset = rowBottom - viewportHeight;
        }

        _ruleScroll.verticalNormalizedPosition = maxOffset <= 0f ? 1f : 1f - Mathf.Clamp(offset, 0f, maxOffset) / maxOffset;
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
        if (field.TagKey != null) return field.TagKey;
        if (field.FeatureId != null) return Window.Text.Get(field.NameKey, field.FeatureId);
        return Window.Text.Get(field.NameKey);
    }

    private string DescriptionOf(StackingField field)
    {
        if (field.TagKey != null) return string.Empty;
        return field.FeatureId != null
            ? Window.Text.Get(field.DescriptionKey, field.FeatureId)
            : Window.Text.Get(field.DescriptionKey);
    }

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
