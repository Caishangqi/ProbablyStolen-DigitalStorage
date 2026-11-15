using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProbablyStolen.DigitalStorage.Client.Workbench;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using static ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Widgets.RuleCardWidgets;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed partial class PartitionWindow
{
    private const float ControlHeight = RuleCardWidgets.ControlHeight;
    private const float FieldWidth = 108f;
    private const float OperatorWidth = 76f;
    private const float ValueWidth = 140f;
    private const float NumberWidth = 120f;
    private const float RangeWidth = 56f;
    private const float UnitWidth = 32f;
    private const int CardsBeforeScroll = 4;
    private readonly List<Image> _detailLayers = new();
    private PopupDropdown? _dropdown;
    private RectTransform? _summaryBox;
    private ScrollRect? _detailScroll;

    private void BuildDetail(CustomUIBuilder builder)
    {
        _detailScroll = null;
        var rule = _detail!;
        BuildHeader(builder, rule);

        builder.BeginRow(Gap);
        builder.AddLabel(_text.Get("partition_all_conditions"));
        SetWidth(builder.Last, BodyWidth - Gap - CountWidth);
        builder.AddLabel(_text.Get("partition_condition_count", rule.Rows.Count));
        SetWidth(builder.Last, CountWidth);
        if (builder.Last.text != null)
        {
            builder.Last.text.alignment = TextAlignmentOptions.TopRight;
            builder.Last.text.color = MachineryStyle.BUTTON_TEXT_DISABLED;
        }
        builder.End();

        var cardHeight = ControlHeight + 2 * CardPadding;
        var cardWidth = BodyWidth;
        if (rule.Rows.Count > CardsBeforeScroll)
        {
            var parent = builder.CurrentContainer;
            builder.BeginScroll(CardsBeforeScroll * (cardHeight + Gap) - Gap);
            // The scroll view stretches to the window's content width; keep it to the body's, beside the boxes.
            var view = parent.GetChild(parent.childCount - 1).gameObject;
            _detailScroll = view.GetComponentInChildren<ScrollRect>(true);
            FixWidth(view, BodyWidth);
            cardWidth = BodyWidth - ScrollbarSpace(view);
        }
        else builder.BeginColumn(Gap);
        for (var index = 0; index < rule.Rows.Count; index++) BuildCard(builder, rule, index, cardWidth);
        builder.End();

        builder.BeginRow(Gap, TextAnchor.MiddleLeft);
        FixWidth(builder.CurrentContainer.gameObject, BodyWidth);
        CustomUIElement? add = null;
        add = TerminalButtonView.AddIcon(builder, Icons, ButtonIcons.Add, false,
            () => OpenFieldPicker(add!, -1), ControlHeight);
        TerminalTooltipPresenter.SetText(add, _text.Get("partition_add_condition"));
        builder.AddFlexibleSpace();
        var sample = TerminalButtonView.AddIcon(builder, Icons, ButtonIcons.SampleAttributes, _showSample, () =>
        {
            _showSample = !_showSample;
            RefreshActiveDetail();
        }, ControlHeight);
        TerminalTooltipPresenter.SetText(sample, _text.Get("partition_sample_attributes"));
        builder.End();

        BuildSummary(builder, rule);
        if (_showSample) BuildSampleTable(builder);
    }

    private void RestoreDetailScroll(float offset)
    {
        if (_detailScroll == null || _detailScroll.content == null || _detailScroll.viewport == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_detailScroll.transform);
        _detailScroll.StopMovement();
        var content = _detailScroll.content;
        var maxOffset = Mathf.Max(0f, content.rect.height - _detailScroll.viewport.rect.height);
        var clampedOffset = PartitionViewRefreshCoordinator.ClampScrollOffset(offset, content.rect.height,
            _detailScroll.viewport.rect.height);
        _detailScroll.verticalNormalizedPosition = maxOffset <= 0f ? 1f : 1f - clampedOffset / maxOffset;
    }

    private void BuildHeader(CustomUIBuilder builder, RuleDraft rule)
    {
        builder.BeginRow(6f, TextAnchor.MiddleLeft);
        builder.AddPointerButton(string.Empty, Back);
        var back = builder.Last;
        SetSize(back, ControlHeight, ControlHeight);
        var arrow = PixelArrow.Add(back.root.transform, PixelArrow.Direction.Left, MachineryStyle.BUTTON_TEXT);
        arrow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        TerminalTooltipPresenter.SetText(back, _text.Get("partition_back"));

        AddRulePicture(builder.CurrentContainer);

        builder.AddInput(_text.Get("partition_rule_name"), rule.Name ?? string.Empty, value =>
        {
            rule.Name = value;
            SaveDetail();
            // A failed save leaves the detail page; redraw so later edits never reach a discarded copy.
            if (_detail == null) Render();
        }, "rule_name");
        SetSize(builder.Last, BodyWidth - 2 * 6f - ControlHeight - CellPixels, ControlHeight);
        InputEllipsis.Attach(builder.Last);
        builder.End();
    }

    private void AddRulePicture(Transform parent)
    {
        var slot = new GameObject("partition_rule_picture", typeof(RectTransform)).AddComponent<Image>();
        slot.transform.SetParent(parent, false);
        slot.sprite = MachineryStyle.Cell;
        slot.type = Image.Type.Sliced;
        slot.pixelsPerUnitMultiplier = 0.5f;
        slot.raycastTarget = false;
        var layout = slot.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = layout.minHeight = layout.preferredHeight = CellPixels;
        var icon = new GameObject("partition_rule_icon", typeof(RectTransform)).AddComponent<Image>();
        icon.transform.SetParent(slot.transform, false);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        Stretch(icon.rectTransform);
        var view = (RectTransform)new GameObject("partition_rule_view", typeof(RectTransform)).transform;
        view.SetParent(slot.transform, false);
        Stretch(view);
        _detailLayers.Clear();
        _pictures.Show(view, _detailLayers, icon, Picture(_draft.At(_selected)?.DisplayKey), true);
    }

    private void BuildCard(CustomUIBuilder builder, RuleDraft rule, int index, float width)
    {
        var row = rule.Rows[index];
        RuleCardWidgets.BeginCard(builder, Gap, width);

        CustomUIElement? field = null;
        field = PopupDropdown.AddField(builder, FieldName(row.Field), FieldWidth, () => OpenFieldPicker(field!, index));

        var operators = row.Field.Operators;
        var operatorLabels = operators.Select(op => new DropdownOption(_text.Get(row.Field.Kind == PartitionFieldKind.Bool
            ? (op == PartitionOperator.Is ? "partition_op_yes" : "partition_op_no")
            : "partition_op_" + op.ToString().ToLowerInvariant()))).ToList();
        var currentOperator = Math.Max(0, IndexOf(operators, row.Operator));
        CustomUIElement? op = null;
        if (operatorLabels.Count > 0)
            op = PopupDropdown.AddField(builder, operatorLabels[currentOperator].Label, OperatorWidth, () =>
            Toggle(op!, operatorLabels, currentOperator, choice =>
            {
                rule.SetOperator(index, operators[choice]);
                SaveDetail();
                RefreshActiveDetail();
            }));

        switch (row.Field.Kind)
        {
            case PartitionFieldKind.Enum:
                var values = row.Field.EnumValues!;
                AddChoice(builder, rule, index, values.Select(value => EnumText(row.Field, value)).ToList(), values,
                    row.Target?.Text, searchable: false);
                break;
            case PartitionFieldKind.TypeSet:
                var types = TypeChoices(row.Target?.Text);
                AddChoice(builder, rule, index, types.Select(TypeName).ToList(), types, row.Target?.Text, searchable: true);
                break;
            case PartitionFieldKind.Number:
                var range = row.Operator == PartitionOperator.Range;
                CustomUIElement? lower = null;
                builder.AddInput(_text.Get("partition_value_placeholder"), NumberText(row.Target), value =>
                {
                    rule.SetTarget(index, ParseNumber(value));
                    CommitValue(lower, NumberText(rule.Rows[index].Target));
                });
                lower = builder.Last;
                SetSize(builder.Last, range ? RangeWidth : NumberWidth, ControlHeight);
                InputEllipsis.Attach(builder.Last);
                if (range)
                {
                    builder.AddLabel("–");
                    SetSize(builder.Last, 10f, ControlHeight);
                    if (builder.Last.text != null) builder.Last.text.alignment = TextAlignmentOptions.Center;
                    CustomUIElement? upper = null;
                    builder.AddInput(_text.Get("partition_upper_placeholder"), NumberText(row.Upper), value =>
                    {
                        rule.SetUpper(index, ParseNumber(value));
                        CommitValue(upper, NumberText(rule.Rows[index].Upper));
                    });
                    upper = builder.Last;
                    SetSize(builder.Last, RangeWidth, ControlHeight);
                    InputEllipsis.Attach(builder.Last);
                }
                if (row.Field.UnitKey != null)
                {
                    builder.AddLabel(_text.Get(row.Field.UnitKey));
                    SetSize(builder.Last, UnitWidth, ControlHeight);
                    Ellipsis(builder.Last);
                }
                break;
            case PartitionFieldKind.ItemId:
                CustomUIElement? id = null;
                builder.AddInput(_text.Get("partition_value_placeholder"), row.Target?.Text ?? string.Empty, value =>
                {
                    rule.SetTarget(index, string.IsNullOrWhiteSpace(value) ? null : PartitionValue.Of(value.Trim()));
                    CommitValue(id, rule.Rows[index].Target?.Text ?? string.Empty);
                });
                id = builder.Last;
                SetSize(builder.Last, ValueWidth, ControlHeight);
                InputEllipsis.Attach(builder.Last);
                break;
        }

        builder.AddFlexibleSpace();
        var delete = TerminalButtonView.AddIcon(builder, Icons, "button_delete", false, () =>
        {
            rule.Remove(index);
            SaveDetail();
            RefreshActiveDetail();
        }, ControlHeight);
        TerminalTooltipPresenter.SetText(delete, _text.Get("partition_remove_condition_tip"));
        builder.End();
    }

    /// <summary>A value chosen from a list; the first entry asks the player to choose and clears the value.</summary>
    private void AddChoice(CustomUIBuilder builder, RuleDraft rule, int index, IReadOnlyList<string> labels,
        IReadOnlyList<string> values, string? current, bool searchable)
    {
        var options = new List<DropdownOption> { new(_text.Get("partition_choose")) };
        options.AddRange(labels.Select(label => new DropdownOption(label)));
        var selected = current == null ? 0 : Math.Max(0, IndexOf(values, current) + 1);
        CustomUIElement? field = null;
        field = PopupDropdown.AddField(builder, options[selected].Label, ValueWidth, () =>
            Toggle(field!, options, selected, choice =>
            {
                rule.SetTarget(index, choice == 0 ? null : PartitionValue.Of(values[choice - 1]));
                SaveDetail();
                RefreshActiveDetail();
            }, searchable ? _text.Get("partition_search_value") : null));
    }

    /// <summary>Opens the field list for a condition, or for a new condition when <paramref name="row"/> is -1.</summary>
    private void OpenFieldPicker(CustomUIElement anchor, int row)
    {
        var fields = MenuFields().ToList();
        var options = fields.Select(field => new DropdownOption(FieldName(field), SampleText(field))).ToList();
        var current = row < 0 || _detail == null ? -1 : fields.FindIndex(field => field.Id == _detail.Rows[row].Field.Id);
        Toggle(anchor, options, current, choice =>
        {
            if (_detail == null) return;
            var id = fields[choice].Id;
            if (row < 0) _detail.AddCondition(id);
            else _detail.ChangeField(row, id);
            SaveDetail();
            RefreshActiveDetail();
        }, _text.Get("partition_search"));
    }

    private void Toggle(CustomUIElement field, IReadOnlyList<DropdownOption> options, int selected, Action<int> choose,
        string? searchPlaceholder = null)
    {
        if (_dropdown == null || !_session.IsActive) return;
        if (_dropdown.IsOpenFor(field)) _dropdown.Close();
        else _dropdown.Open(field, options, selected, choose, searchPlaceholder);
    }

    // Losing focus commits the field before another control receives its click.
    // Redrawing only the summary keeps that control alive until the click completes.
    private void CommitValue(CustomUIElement? input, string stored)
    {
        // Disabling a focused field during a redraw can end its edit; the redraw then owns the page.
        if (_rendering) return;
        input?.input?.SetTextWithoutNotify(stored);
        SaveDetail();
        // A failed save leaves the detail page; redraw so later edits never reach a discarded copy.
        if (_detail == null || _summaryBox == null || _window == null)
        {
            Render();
            return;
        }

        CustomUIBuilder.ClearContainer(_summaryBox);
        FillSummary(new CustomUIBuilder(CustomUIManager.Instance, _window, _summaryBox), _detail);
        FitHeight();
    }

    private void BuildSummary(CustomUIBuilder builder, RuleDraft rule)
    {
        _summaryBox = BeginSpecBox(builder);
        FillSummary(builder, rule);
        builder.End();
    }

    private void FillSummary(CustomUIBuilder builder, RuleDraft rule)
    {
        var issue = rule.Validate();
        AddWrapped(builder, issue == null
                ? _text.Get("partition_summary_label", Summary(rule.Complete()))
                : _text.Get("partition_not_saved", _text.Get("partition_issue_" + issue.Value.ToString().ToLowerInvariant())),
            issue == null ? MachineryStyle.VALUE_TEXT : MachineryStyle.PROGRESS_FILL_DANGER);
        foreach (var warning in rule.Lint())
            AddWrapped(builder, _text.Get("partition_warning_" + warning.Kind.ToString().ToLowerInvariant()),
                MachineryStyle.PROGRESS_FILL_WARNING);
    }

    private void AddWrapped(CustomUIBuilder builder, string text, Color color)
    {
        builder.AddLabel(text);
        SetWidth(builder.Last, BodyWidth - 20f);
        if (builder.Last.text == null) return;
        builder.Last.text.color = color;
        builder.Last.text.enableWordWrapping = true;
    }

    private void BuildSampleTable(CustomUIBuilder builder)
    {
        var stripe = false;
        foreach (var field in MenuFields())
        {
            var value = SampleText(field);
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            if (stripe) Fill(builder.CurrentContainer.gameObject, RuleCardWidgets.StripeFill);
            stripe = !stripe;
            if (builder.CurrentContainer.GetComponent<HorizontalLayoutGroup>() is { } line)
                line.padding = new RectOffset(8, 8, 2, 2);
            builder.AddLabel(FieldName(field));
            SetWidth(builder.Last, 150f);
            Ellipsis(builder.Last);
            builder.AddLabel(value.Length == 0 ? _text.Get("partition_unknown_value") : value);
            SetWidth(builder.Last, BodyWidth - 150f - 8f - 16f);
            Ellipsis(builder.Last);
            if (builder.Last.text != null)
                builder.Last.text.color = value.Length == 0 ? MachineryStyle.BUTTON_TEXT_DISABLED : MachineryStyle.VALUE_TEXT;
            builder.End();
        }
    }

    private IEnumerable<PartitionField> MenuFields()
    {
        foreach (var field in PartitionFieldRegistry.All) yield return field;
        var summary = _detail == null || _draft.At(_selected)?.DisplayKey is not { } key ? null : key.ReadSummary();
        if (summary == null) yield break;
        foreach (var feature in summary.Features.Where(feature => feature.Exposed && !feature.Disabled))
            yield return PartitionFieldRegistry.Find(PartitionFieldRegistry.FeatureField(feature.Identifier))!;
    }
}
