using System;
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

/// <summary>Manages saved item rules and preserves the list state while an item's editor is open.</summary>
internal sealed class StackingRulesScreen : SubScreen
{
    private const float Gap = 4f;
    private const float ColorInputWidth = 84f;
    private const float CountWidth = 110f;
    private const float ToggleWidth = 72f;
    private const float PickerWidth = 320f;
    // The list keeps this many rows of height whatever it holds, so the page does not resize as rules come and go.
    private const int VisibleRows = 6;
    private const float RowHeight = RuleCardWidgets.ControlHeight + 2 * RuleCardWidgets.CardPadding;

    private readonly StackingRulesEditor _editor;
    private readonly StorageTerminalServices _storage;
    private readonly IStackingPlatform _platform;
    private PopupDropdown? _dropdown;
    private TerminalInventoryRefreshHandler? _frame;
    private RectTransform? _rowHost;
    private ScrollRect? _rowScroll;
    private StackingItemNames? _names;
    private string _query = string.Empty;
    private float? _pendingScroll;

    internal StackingRulesScreen(TerminalWindowHost window, StorageTerminalServices storage, IStackingPlatform platform)
        : base(window)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        var data = window.CurrentContext?.FeatureData ?? throw new InvalidOperationException("The window is not open.");
        _editor = new StackingRulesEditor(StackingProjector.StoreOf(data), window.StoragePipeline.Invalidate);
    }

    /// <inheritdoc />
    public override string ScreenId => StackingFeature.RulesScreenId;


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
        }

        _rowHost = null;
        _rowScroll = null;
        _names = new StackingItemNames(Window, _storage);
        if (!_editor.IsEditable)
        {
            builder.AddWarning(text.Get("stacking_config_unreadable"));
            return true;
        }

        var config = _editor.Config;
        BuildMarker(builder, config);
        builder.AddToggle(text.Get("stacking_advanced"), config.Advanced, on => Edit(() => _editor.SetAdvanced(on)));
        TerminalTooltipPresenter.SetText(builder.Last, text.Get("stacking_advanced_tip"));
        BuildListHeader(builder, config);
        builder.AddInput(text.Get("stacking_item_search"), _query, onTyping: typed =>
        {
            _query = typed ?? string.Empty;
            RebuildRows();
            Window.FitSubScreenContent(this);
        });
        RuleCardWidgets.SetSize(builder.Last, BodyWidth, RuleCardWidgets.ControlHeight);
        builder.BeginColumn(Gap);
        _rowHost = builder.CurrentContainer;
        RebuildRows();
        builder.End();
        BuildButtons(builder, config);
        builder.AddLabel(text.Get(_editor.LastError ?? "stacking_saved_immediately"));
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

    /// <summary>Shows the rules as last saved, for example by the editor page, at the same search and scroll.</summary>
    internal override void OnRevealed() => Edit(() => true);

    internal override void Dispose()
    {
        _dropdown?.Close();
        _dropdown = null;
        _rowHost = null;
        _rowScroll = null;
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
            if (builder.Last.image != null) builder.Last.image.color = StackingPageText.Tint(config.Marker);
        }

        foreach (var preset in StackingMarker.Presets)
        {
            var color = preset;
            builder.AddPointerButton(string.Empty, () => Edit(() => _editor.SetMarkerColor(color.Format())));
            RuleCardWidgets.SetSize(builder.Last, RuleCardWidgets.ControlHeight, RuleCardWidgets.ControlHeight);
            if (builder.Last.image != null)
            {
                builder.Last.image.sprite = null;
                builder.Last.image.color = StackingPageText.Tint(color);
            }

            TerminalTooltipPresenter.SetButtonText(builder.Last, color.Format());
        }

        builder.End();
    }

    private void BuildListHeader(CustomUIBuilder builder, StackingConfig config)
    {
        var text = Window.Text;
        builder.BeginRow(Gap);
        builder.AddLabel(text.Get("stacking_item_rules"));
        RuleCardWidgets.FixWidth(builder.Last.root, BodyWidth - Gap - CountWidth);
        // The bulk actions apply to every saved rule, so the count stays the total while searching.
        builder.AddLabel(text.Get("stacking_item_rule_count", config.Items.Count));
        RuleCardWidgets.FixWidth(builder.Last.root, CountWidth);
        if (builder.Last.text != null)
        {
            builder.Last.text.alignment = TMPro.TextAlignmentOptions.TopRight;
            builder.Last.text.color = MachineryStyle.BUTTON_TEXT_DISABLED;
        }

        builder.End();
    }

    private void RebuildRows()
    {
        if (_rowHost == null || _names == null) return;
        _dropdown?.Close();
        CustomUIBuilder.ClearContainer(_rowHost);
        _rowScroll = null;
        var names = _names;
        var items = StackingRulesEditor.FindItems(_editor.Config, _query, names.Of, _storage.Search.MatchesText);
        var builder = new CustomUIBuilder(CustomUIManager.Instance, Window.CustomWindow, _rowHost);
        RuleCardWidgets.BeginListBox(builder, Gap, BodyWidth, RuleCardWidgets.CardFill);
        var inner = BodyWidth - 2 * RuleCardWidgets.ListPadding;
        var parent = builder.CurrentContainer;
        builder.BeginScroll(VisibleRows * (RowHeight + Gap) - Gap);
        var view = parent.GetChild(parent.childCount - 1).gameObject;
        var rowWidth = RuleCardWidgets.ConfigureListScroll(view, inner, Gap, items.Count > VisibleRows);
        _rowScroll = view.GetComponentInChildren<ScrollRect>(true);

        foreach (var item in items) BuildRow(builder, item, rowWidth, names);
        if (items.Count == 0)
        {
            builder.AddLabel(Window.Text.Get(_query.Trim().Length != 0 ? "stacking_item_search_empty" : "stacking_items_empty"));
            RuleCardWidgets.FixWidth(builder.Last.root, rowWidth);
            StackingPageText.Muted(builder.Last);
        }

        builder.End();
        builder.End();
    }

    private void BuildRow(CustomUIBuilder builder, StackingItemRule item, float width, StackingItemNames names)
    {
        var text = Window.Text;
        var itemId = item.ItemId;
        RuleCardWidgets.BeginCard(builder, Gap, width);
        var iconsWidth = 2 * (RuleCardWidgets.ControlHeight + Gap);
        builder.AddLabel(TooltipLiteralText.ForRichText(names.Of(itemId)));
        RuleCardWidgets.FixWidth(builder.Last.root, width - 2 * RuleCardWidgets.CardPadding - ToggleWidth - iconsWidth - Gap);
        RuleCardWidgets.Ellipsis(builder.Last);
        var lore = TooltipLiteralText.ForRichText(itemId);
        if (!names.IsStored(itemId)) lore += "\n" + text.Get("stacking_item_absent");
        TerminalTooltipPresenter.SetText(builder.Last, lore);
        builder.AddPointerButton(text.Get(item.Enabled ? "stacking_item_on" : "stacking_item_off"),
            () => Edit(() => _editor.SetItemEnabled(itemId, !item.Enabled)));
        RuleCardWidgets.SetSize(builder.Last, ToggleWidth, RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(builder.Last, text.Get("stacking_item_switch_tip"));
        var edit = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, "button_edit", false,
            () => OpenEditor(itemId), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(edit, text.Get("stacking_item_edit_tip"));
        var delete = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, "button_delete", false,
            () => Edit(() => _editor.RemoveItem(itemId)), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(delete, text.Get("stacking_item_delete_tip"));
        builder.End();
    }

    private void BuildButtons(CustomUIBuilder builder, StackingConfig config)
    {
        var text = Window.Text;
        var any = config.Items.Count > 0;
        builder.BeginRow(Gap, TextAnchor.MiddleLeft);
        RuleCardWidgets.FixWidth(builder.CurrentContainer.gameObject, BodyWidth);
        builder.AddPointerButton(text.Get("stacking_all_on"), () => Edit(() => _editor.SetAllEnabled(true)));
        if (builder.Last.button != null) builder.Last.button.interactable = any;
        builder.AddPointerButton(text.Get("stacking_all_off"), () => Edit(() => _editor.SetAllEnabled(false)));
        if (builder.Last.button != null) builder.Last.button.interactable = any;
        builder.AddPointerButton(text.Get("stacking_all_delete"), () => Edit(() => _editor.RemoveAll()));
        if (builder.Last.button != null) builder.Last.button.interactable = any;
        builder.AddFlexibleSpace();
        CustomUIElement? add = null;
        add = TerminalButtonView.AddIcon(builder, Window.ButtonIcons, ButtonIcons.Add, false,
            () => OpenItemPicker(add!), RuleCardWidgets.ControlHeight);
        TerminalTooltipPresenter.SetButtonText(add, text.Get("stacking_item_add"));
        builder.End();
    }

    /// <summary>Lists the stored items by name; choosing one opens its rule, or a new empty draft.</summary>
    private void OpenItemPicker(CustomUIElement anchor)
    {
        if (_dropdown == null || _names == null || !((ITerminalViewHost)Window).IsCurrentSession()) return;
        if (_dropdown.IsOpenFor(anchor))
        {
            _dropdown.Close();
            return;
        }

        var names = _names;
        var config = _editor.Config;
        var items = StackingRulesEditor.StoredItems(StackingPageText.StoredKeys(Window, _storage))
            .OrderBy(id => names.Of(id), StringComparer.CurrentCulture).ThenBy(id => id, StringComparer.Ordinal).ToArray();
        var existing = Window.Text.Get("stacking_item_has_rule");
        var labels = items.Select(id => new DropdownOption(TooltipLiteralText.ForRichText(names.Of(id)),
            config.Find(id) != null ? existing : TooltipLiteralText.ForRichText(id), names.Of(id) + " " + id)).ToArray();
        _dropdown.Open(anchor, labels, -1, choice => OpenEditor(items[choice]), Window.Text.Get("stacking_item_search"),
            preferredWidth: PickerWidth);
    }

    private void OpenEditor(string itemId)
    {
        _dropdown?.Close();
        if (!((ITerminalViewHost)Window).IsCurrentSession()) return;
        if (_platform.CreateItemEditor(Window, itemId) is SubScreen editor) Window.PushScreen(editor);
    }

    private void Edit(Func<bool> edit)
    {
        _dropdown?.Close();
        var offset = _rowScroll?.content?.anchoredPosition.y ?? 0f;
        edit();
        RefreshContent();
        Window.FitSubScreenContent(this);
        RestoreScroll(offset);
        // Restore after the window refits the list and resets its scroll position.
        _pendingScroll = offset;
    }

    private void Tick()
    {
        _dropdown?.CloseOnOutsidePress(StackingPageText.EventCamera(Window));
        if (_pendingScroll is not { } offset) return;
        _pendingScroll = null;
        RestoreScroll(offset);
    }

    private void RestoreScroll(float offset)
    {
        if (_rowScroll == null || _rowScroll.content == null || _rowScroll.viewport == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_rowScroll.transform);
        _rowScroll.StopMovement();
        var maxOffset = Mathf.Max(0f, _rowScroll.content.rect.height - _rowScroll.viewport.rect.height);
        _rowScroll.verticalNormalizedPosition = maxOffset <= 0f ? 1f : 1f - Mathf.Clamp(offset, 0f, maxOffset) / maxOffset;
    }
}
