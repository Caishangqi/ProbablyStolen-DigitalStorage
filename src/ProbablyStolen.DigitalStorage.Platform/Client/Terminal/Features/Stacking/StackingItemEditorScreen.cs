using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Stacking;

/// <summary>Screen for a single item rule. Changes remain in the draft until Apply succeeds.</summary>
internal sealed class StackingItemEditorScreen : SubScreen
{
    private const float Gap = 4f;
    private const float FieldWidth = 224f;
    private const float FieldPickerWidth = 320f;
    private const float ModeWidth = 96f;
    private const float CountWidth = 110f;
    // The list keeps this many rows of height whatever it holds, so the page does not resize as rows come and go.
    private const int VisibleCards = 4;
    private const float CardHeight = RuleCardWidgets.ControlHeight + 2 * RuleCardWidgets.CardPadding;
    private static readonly Color LocatedCardFill = new(1f, 1f, 1f, 0.16f);
    private static readonly StackingMode[] Modes = { StackingMode.Match, StackingMode.Ignore };

    private readonly StackingRulesEditor _editor;
    private readonly StorageTerminalServices _storage;
    private readonly StackingItemDraft _draft;
    private readonly string _itemName;
    private PopupDropdown? _dropdown;
    private TerminalInventoryRefreshHandler? _frame;
    private IDisposable? _networkSubscription;
    private bool _visibilityDirty;
    private RectTransform? _cardHost;
    private ScrollRect? _ruleScroll;
    private int? _revealedPosition;
    private string? _revealedFieldId;
    private string _ruleQuery = string.Empty;
    private HashSet<string>? _visibleFieldIds;
    private float? _pendingScroll;

    internal StackingItemEditorScreen(TerminalWindowHost window, StorageTerminalServices storage, string itemId) : base(window)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        var data = window.CurrentContext?.FeatureData ?? throw new InvalidOperationException("The window is not open.");
        _editor = new StackingRulesEditor(StackingProjector.StoreOf(data), window.StoragePipeline.Invalidate);
        _draft = new StackingItemDraft(_editor.Config, itemId);
        _itemName = new StackingItemNames(window, storage).Of(itemId);
    }

    /// <inheritdoc />
    public override string ScreenId => StackingFeature.ItemEditorScreenId;


    internal override bool FitsContent => true;

    private static float BodyWidth => TerminalWindowMetrics.GridWidth + 36f;

    protected override bool BuildContent(CustomUIBuilder builder)
    {
        var text = Window.Text;
        _dropdown ??= new PopupDropdown(Window.CustomWindow, _storage.Search.MatchesText);
        if (_frame == null)
        {
            _frame = Window.CustomWindow.root.AddComponent<TerminalInventoryRefreshHandler>();
            _frame.Initialize(Tick);
            _networkSubscription = _storage.SubscribeNetwork(_ => _visibilityDirty = true);
        }

        _cardHost = null;
        _ruleScroll = null;
        builder.AddHeader(_itemName);
        RuleCardWidgets.FixWidth(builder.Last.root, BodyWidth);
        RuleCardWidgets.Ellipsis(builder.Last);
        builder.AddLabel(_draft.ItemId);
        RuleCardWidgets.FixWidth(builder.Last.root, BodyWidth);
        StackingPageText.Muted(builder.Last);
        if (!_editor.IsEditable)
        {
            builder.AddWarning(text.Get("stacking_config_unreadable"));
            return true;
        }

        BuildListHeader(builder);
        builder.BeginRow(Gap, TextAnchor.MiddleLeft);
        RuleCardWidgets.FixWidth(builder.CurrentContainer.gameObject, BodyWidth);
        builder.AddInput(text.Get("stacking_rule_search"), _ruleQuery, onTyping: typed =>
        {
            _ruleQuery = typed ?? string.Empty;
            _revealedFieldId = null;
            RebuildCards();
            Window.FitSubScreenContent(this);
        });
        RuleCardWidgets.SetSize(builder.Last, BodyWidth - Gap - RuleCardWidgets.ControlHeight, RuleCardWidgets.ControlHeight);
        CustomUIElement? add = null;
        add = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, ButtonIcons.Add, false,
            () => OpenFieldPicker(add!, -1), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(add, text.Get("stacking_add_rule"));
        builder.End();
        builder.BeginColumn(Gap);
        _cardHost = builder.CurrentContainer;
        RebuildCards();
        builder.End();
        BuildButtons(builder);
        builder.AddLabel(text.Get(_editor.LastError ?? "stacking_draft_hint"));
        StackingPageText.Muted(builder.Last);
        return true;
    }

    public override bool HandleBack()
    {
        if (_dropdown?.IsOpen != true) return false;
        _dropdown.Close();
        return true;
    }

    internal override void OnCovered() => _dropdown?.Close();

    internal override void Dispose()
    {
        _dropdown?.Close();
        _dropdown = null;
        _cardHost = null;
        _ruleScroll = null;
        _networkSubscription?.Dispose();
        _networkSubscription = null;
        _frame?.Detach();
        if (_frame != null) UnityEngine.Object.Destroy(_frame);
        _frame = null;
        base.Dispose();
    }

    private void BuildListHeader(CustomUIBuilder builder)
    {
        var text = Window.Text;
        builder.BeginRow(Gap);
        builder.AddLabel(text.Get("stacking_conditions"));
        RuleCardWidgets.FixWidth(builder.Last.root, BodyWidth - Gap - CountWidth);
        builder.AddLabel(text.Get("stacking_rule_count", _draft.Rules.Count));
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
        _visibilityDirty = false;
        _visibleFieldIds = _editor.Config.Advanced ? null : new HashSet<string>(
            _draft.MenuOptions(StackingPageText.StoredKeys(Window, _storage), includeHidden: false)
                .Select(option => option.Field.Id), StringComparer.Ordinal);
        var rows = StackingRulesEditor.FindRows(_draft.Rules, _ruleQuery, FieldName, _storage.Search.MatchesText,
            _revealedFieldId, ShowsIdentifier);
        var builder = new CustomUIBuilder(CustomUIManager.Instance, Window.CustomWindow, _cardHost);
        RuleCardWidgets.BeginListBox(builder, Gap, BodyWidth, RuleCardWidgets.CardFill);
        var inner = BodyWidth - 2 * RuleCardWidgets.ListPadding;
        var parent = builder.CurrentContainer;
        builder.BeginScroll(VisibleCards * (CardHeight + Gap) - Gap);
        var view = parent.GetChild(parent.childCount - 1).gameObject;
        var cardWidth = RuleCardWidgets.ConfigureListScroll(view, inner, Gap, rows.Count > VisibleCards);
        _ruleScroll = view.GetComponentInChildren<ScrollRect>(true);

        for (var position = 0; position < rows.Count; position++)
        {
            var row = rows[position];
            var located = _draft.Rules[row].FieldId == _revealedFieldId;
            if (located) _revealedPosition = position;
            BuildCard(builder, row, cardWidth, located);
        }

        if (rows.Count == 0)
        {
            builder.AddLabel(Window.Text.Get(_ruleQuery.Trim().Length != 0 ? "stacking_rule_search_empty" : "stacking_rules_empty"));
            RuleCardWidgets.FixWidth(builder.Last.root, cardWidth);
            StackingPageText.Muted(builder.Last);
        }

        builder.End();
        builder.End();
    }

    private void BuildCard(CustomUIBuilder builder, int row, float width, bool located)
    {
        var text = Window.Text;
        var rule = _draft.Rules[row];
        RuleCardWidgets.BeginCard(builder, Gap, width);
        if (located) RuleCardWidgets.Fill(builder.CurrentContainer.gameObject, LocatedCardFill);
        var fieldName = TooltipLiteralText.ForRichText(FieldName(rule.FieldId));
        CustomUIElement? field = null;
        field = PopupDropdown.AddField(builder, fieldName, FieldWidth, () => OpenFieldPicker(field!, row));
        var description = ShowsIdentifier(rule.FieldId) && StackingFieldCatalog.Find(rule.FieldId) is { } attribute
            ? StackingPageText.Description(text, attribute) : string.Empty;
        var lore = new[] { fieldName, TooltipLiteralText.ForRichText(description) }
            .Concat(StackingRuleSummary.RowNotes(_draft.Rules, row).Select(note => text.Get(note)))
            .Where(line => !string.IsNullOrEmpty(line));
        TerminalTooltipPresenter.SetButtonText(field, string.Join("\n", lore));
        var modeLabels = Modes.Select(mode => new DropdownOption(text.Get(StackingPageText.ModeKey(mode)))).ToList();
        var current = Array.IndexOf(Modes, rule.Mode);
        CustomUIElement? mode = null;
        mode = PopupDropdown.AddField(builder, modeLabels[current].Label, ModeWidth, () =>
            Toggle(mode!, modeLabels, current, choice => Edit(() =>
            {
                _draft.SetMode(row, Modes[choice]);
                return true;
            })));
        builder.AddFlexibleSpace();
        var delete = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, "button_delete", false,
            () => Edit(() =>
            {
                _draft.Remove(row);
                return true;
            }), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(delete, text.Get("stacking_remove_rule_tip"));
        builder.End();
    }

    private void BuildButtons(CustomUIBuilder builder)
    {
        var text = Window.Text;
        builder.BeginRow(Gap, TextAnchor.MiddleLeft);
        RuleCardWidgets.FixWidth(builder.CurrentContainer.gameObject, BodyWidth);
        builder.AddPointerButton(text.Get("stacking_clear_rules"), () => Edit(() =>
        {
            _draft.Clear();
            return true;
        }));
        if (builder.Last.button != null) builder.Last.button.interactable = _draft.Rules.Count > 0;
        builder.AddFlexibleSpace();
        builder.AddPointerButton(text.Get("stacking_apply"), Apply);
        builder.End();
    }

    /// <summary>Saves the draft and returns to the page below; a refused save keeps the page and the draft.</summary>
    private void Apply()
    {
        _dropdown?.Close();
        if (_editor.Apply(_draft.ToRule()))
        {
            Window.PopScreen();
            return;
        }

        RefreshContent();
        Window.FitSubScreenContent(this);
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

        var text = Window.Text;
        var options = _draft.MenuOptions(StackingPageText.StoredKeys(Window, _storage), _editor.Config.Advanced);
        var absence = text.Get("stacking_field_absent");
        var hidden = text.Get("stacking_field_hidden");
        var names = options.Select(option => StackingPageText.FieldName(text, option.Field.Id)).ToArray();
        var details = options.Select((option, index) => (Title: TooltipLiteralText.ForRichText(names[index]),
            Description: TooltipLiteralText.ForRichText(StackingPageText.Description(text, option.Field)),
            Absence: option.AbsentFromInventory ? absence : option.Hidden ? hidden : null)).ToArray();
        var labels = details.Select((detail, index) =>
            new DropdownOption(detail.Title, detail.Absence ?? detail.Description, names[index])).ToArray();
        var current = row < 0 ? -1 : options.ToList().FindIndex(option => option.Field.Id == _draft.Rules[row].FieldId);
        _dropdown.Open(anchor, labels, current, choice =>
        {
            var id = options[choice].Field.Id;
            Edit(() =>
            {
                var selected = row < 0 ? _draft.AddOrLocate(id) : _draft.ReplaceField(row, id);
                if (selected < 0) return false;
                _revealedFieldId = _draft.Rules[selected].FieldId;
                return true;
            }, reveal: true);
        }, text.Get("stacking_search"), preferredWidth: FieldPickerWidth, hover: new StackingFieldTooltip(details));
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
        // Restore after the window refits the list and resets its scroll position.
        _pendingScroll = _ruleScroll?.content?.anchoredPosition.y ?? offset;
    }

    private void Tick()
    {
        _dropdown?.CloseOnOutsidePress(StackingPageText.EventCamera(Window));
        if (_visibilityDirty && _cardHost != null && Window.IsTopScreen(this) && !_editor.Config.Advanced &&
            ((ITerminalViewHost)Window).IsCurrentSession())
        {
            var currentOffset = _ruleScroll?.content?.anchoredPosition.y ?? 0f;
            RebuildCards();
            Window.FitSubScreenContent(this);
            _pendingScroll = currentOffset;
        }

        if (_pendingScroll is not { } offset) return;
        _pendingScroll = null;
        RestoreRuleScroll(offset, reveal: false);
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

    private bool ShowsIdentifier(string fieldId) => _visibleFieldIds == null ||
        !fieldId.StartsWith(StackingFieldCatalog.FeaturePrefix, StringComparison.Ordinal) || _visibleFieldIds.Contains(fieldId);

    private string FieldName(string fieldId) => ShowsIdentifier(fieldId)
        ? StackingPageText.FieldName(Window.Text, fieldId)
        : Window.Text.Get("stacking_field_hidden");
}
