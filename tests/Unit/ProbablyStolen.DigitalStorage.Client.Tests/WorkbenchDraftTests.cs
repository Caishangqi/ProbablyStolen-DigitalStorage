using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Client.Workbench;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class WorkbenchDraftTests
{
    private static readonly ItemKey Apple = new("item.apple");
    private static readonly ItemKey Pear = new("item.pear");
    private static readonly ItemKey Plum = new("item.plum");

    private static PartitionDraft Draft(int limit = 8, int columns = 4, PartitionConfig? baseline = null)
    {
        var next = 0;
        return new PartitionDraft(baseline ?? PartitionConfig.Empty, limit, columns, () => $"id{next++}");
    }

    [Test]
    public void NearestFree_UsesGridDistanceAndTheSmallerSlotOnATie()
    {
        // 4 columns; slot 5 is row 1 column 1. Slots 1 and 4 and 6 are all one step away; 1 is the smallest.
        Assert.That(RuleGridOps.NearestFree(5, new HashSet<int> { 5 }, 16, 4), Is.EqualTo(1));
        Assert.That(RuleGridOps.NearestFree(5, new HashSet<int> { 1, 4, 5 }, 16, 4), Is.EqualTo(6));
        Assert.That(RuleGridOps.NearestFree(0, new HashSet<int> { 0, 1 }, 2, 4), Is.EqualTo(-1));
    }

    [Test]
    public void AddSample_LocatesAnIdenticalDefaultRuleInsteadOfAddingOne()
    {
        var draft = Draft();
        var first = draft.AddSample(Apple, 2);
        var again = draft.AddSample(Apple, 0);

        Assert.That((first.Status, first.Slot), Is.EqualTo((DraftAddStatus.Added, 2)));
        Assert.That((again.Status, again.Slot), Is.EqualTo((DraftAddStatus.Duplicate, 2)));
        Assert.That(draft.Rules.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddSample_OnAnOccupiedSlotGoesToTheNearestFreeSlot()
    {
        var draft = Draft();
        draft.AddSample(Apple, 0);

        var result = draft.AddSample(Pear, 0);

        Assert.That((result.Status, result.Slot), Is.EqualTo((DraftAddStatus.Added, 1)));
        Assert.That(draft.At(0)!.IsDefaultFor("item.apple"), Is.True, "The rule already there is never replaced.");
    }

    [Test]
    public void Changed_IsRaisedOncePerChangeAndNotForNoOps()
    {
        var draft = Draft();
        var changes = 0;
        draft.Changed += () => changes++;

        draft.AddSample(Apple, 0);
        Assert.That(changes, Is.EqualTo(1), "Adding a rule is saved.");
        draft.AddSample(Apple, 3);
        Assert.That(changes, Is.EqualTo(1), "Locating an identical rule changes nothing.");
        draft.Move(0, 2);
        draft.Move(2, 2);
        Assert.That(changes, Is.EqualTo(2), "A move to the same slot changes nothing.");
        draft.ToggleMode();
        draft.SetMode(PartitionMode.Blacklist);
        Assert.That(changes, Is.EqualTo(3), "Setting the current mode again changes nothing.");
        draft.Duplicate(2);
        draft.Remove(2);
        draft.Undo();
        draft.Clear();
        draft.Clear();
        Assert.That(changes, Is.EqualTo(7), "Duplicate, remove, undo and one clear each change the partition.");
        draft.SetRuleLimit(3);
        Assert.That(changes, Is.EqualTo(7), "The limit is not part of the stored partition.");
    }

    [Test]
    public void MarkSaved_KeepsTheRulesAndTheUndoStack()
    {
        var draft = Draft();
        draft.AddSample(Apple, 0);
        draft.AddSample(Pear, 1);
        draft.Remove(1);

        draft.MarkSaved(draft.ToConfig().With(revision: 4));

        Assert.That(draft.IsDirty, Is.False);
        Assert.That(draft.Baseline.Revision, Is.EqualTo(4));
        Assert.That(draft.CanUndo, Is.True, "Saving after every change must not drop the undo stack.");
        Assert.That(draft.Rules.Count, Is.EqualTo(1));
    }

    [Test]
    public void SetRuleLimit_FollowsModulesAndKeepsRulesAboveALowerLimit()
    {
        var draft = Draft(limit: 2);
        draft.AddSample(Apple, 0);
        draft.AddSample(Pear, 1);
        Assert.That(draft.CanAdd, Is.False);

        draft.SetRuleLimit(4);
        Assert.That((draft.CanAdd, draft.Capacity), Is.EqualTo((true, 4)), "A type limit module adds room at once.");

        draft.SetRuleLimit(1);
        Assert.That((draft.CanAdd, draft.Rules.Count, draft.Capacity), Is.EqualTo((false, 2, 2)),
            "A lower limit keeps every rule and stops additions.");
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => draft.SetRuleLimit(0)));
    }

    [Test]
    public void AdoptRevision_KeepsTheDraftAndOnlyMovesTheBaseRevision()
    {
        var draft = Draft();
        draft.AddSample(Apple, 0);
        draft.ToggleMode();

        draft.AdoptRevision(7);

        Assert.That(draft.Baseline.Revision, Is.EqualTo(7));
        Assert.That(draft.ToConfig().Rules.Count, Is.EqualTo(1), "The unapplied rules stay in the draft.");
        Assert.That(draft.Mode, Is.EqualTo(PartitionMode.Blacklist));
        Assert.That(draft.IsDirty, Is.True, "Only the revision moved; the draft still differs from what is stored.");
    }

    [Test]
    public void LandingSlot_IsWhereTheNextSampleLandsAndMinusOneWhenFull()
    {
        var draft = Draft(limit: 2);
        Assert.That(draft.LandingSlot(1), Is.EqualTo(1), "A free slot is the landing slot.");
        draft.AddSample(Apple, 1);
        var landing = draft.LandingSlot(1);
        Assert.That(landing, Is.EqualTo(0), "An occupied slot lands on the nearest free one.");
        Assert.That(draft.AddSample(Pear, 1).Slot, Is.EqualTo(landing), "The highlight and the add agree.");
        Assert.That(draft.LandingSlot(0), Is.EqualTo(-1), "Nothing lands once the limit is reached.");
    }

    [Test]
    public void Move_ToAnEmptySlotMovesAndToAnOccupiedSlotSwaps()
    {
        var draft = Draft();
        draft.AddSample(Apple, 0);
        draft.AddSample(Pear, 1);
        var appleId = draft.At(0)!.RuleId;

        Assert.That(draft.Move(0, 5), Is.True);
        Assert.That(draft.At(5)!.RuleId, Is.EqualTo(appleId));
        Assert.That(draft.Move(5, 1), Is.True);
        Assert.That(draft.At(1)!.RuleId, Is.EqualTo(appleId));
        Assert.That(draft.At(5)!.IsDefaultFor("item.pear"), Is.True);
        Assert.That(draft.Move(3, 4), Is.False, "An empty slot has nothing to move.");
    }

    [Test]
    public void Duplicate_RenameRemoveClearAndUndo()
    {
        var draft = Draft();
        draft.AddSample(Apple, 0);
        var copy = draft.Duplicate(0);
        Assert.That(copy.Status, Is.EqualTo(DraftAddStatus.Added));
        Assert.That(draft.At(copy.Slot)!.RuleId, Is.Not.EqualTo(draft.At(0)!.RuleId));

        Assert.That(draft.Rename(0, "  fruit  "), Is.True);
        Assert.That(draft.At(0)!.Name, Is.EqualTo("fruit"));
        draft.Rename(0, " ");
        Assert.That(draft.At(0)!.Name, Is.Null);

        draft.Remove(0);
        Assert.That(draft.At(0), Is.Null);
        draft.AddSample(Plum, 0);
        Assert.That(draft.Undo(), Is.True);
        Assert.That(draft.Rules.Count, Is.EqualTo(3), "The removed rule returns beside the rule now in its slot.");

        draft.Clear();
        Assert.That(draft.Rules, Is.Empty);
        draft.Undo();
        Assert.That(draft.Rules.Count, Is.EqualTo(3));
        Assert.That(draft.CanUndo, Is.False);
    }

    [Test]
    public void LoweredLimit_KeepsRulesButStopsAdditions()
    {
        var stored = new PartitionConfig(PartitionMode.Whitelist,
            new[] { Apple, Pear, Plum }.Select((key, slot) => PartitionRule.ForSample($"r{slot}", slot * 3, key)), 4);
        var draft = Draft(limit: 2, baseline: stored);

        Assert.That(draft.Rules.Count, Is.EqualTo(3));
        Assert.That(draft.Capacity, Is.EqualTo(7), "Kept rules beyond the limit stay in their slots.");
        Assert.That(draft.CanAdd, Is.False);
        Assert.That(draft.AddSample(new ItemKey("item.kiwi"), 1).Status, Is.EqualTo(DraftAddStatus.Full));
        Assert.That(draft.Duplicate(0).Status, Is.EqualTo(DraftAddStatus.Full));
        Assert.That(draft.Rename(3, "edited"), Is.True, "Kept rules may still be edited.");
        draft.Remove(0);
        draft.Remove(3);
        Assert.That(draft.CanAdd, Is.True);
    }

    [Test]
    public void Dirty_TracksChangesAgainstTheBaselineAndRebaseAcceptsApplied()
    {
        var draft = Draft();
        Assert.That(draft.IsDirty, Is.False);
        Assert.That(draft.Mode, Is.EqualTo(PartitionMode.Whitelist));
        draft.ToggleMode();
        Assert.That(draft.Mode, Is.EqualTo(PartitionMode.Blacklist));
        Assert.That(draft.IsDirty, Is.True);
        draft.ToggleMode();
        Assert.That(draft.Mode, Is.EqualTo(PartitionMode.Whitelist));
        Assert.That(draft.IsDirty, Is.False);
        draft.AddSample(Apple, 0);
        Assert.That(draft.IsDirty, Is.True);

        var applied = draft.ToConfig().With(revision: 1);
        draft.Rebase(applied);
        Assert.That(draft.IsDirty, Is.False);
        Assert.That(draft.ToConfig().Revision, Is.EqualTo(1));
    }

    [Test]
    public void RuleDraft_StartsConditionsFromVisibleSampleValuesOnly()
    {
        var sample = new ItemAttributeSet(new Dictionary<string, AttributeValue>
        {
            ["item"] = AttributeValue.Of("item.poison"),
            ["heat"] = AttributeValue.Of(40L),
            ["type"] = AttributeValue.OfSet(new[] { "POISON", "DRUG" }),
            ["legality"] = AttributeValue.Of("high")
        }, hasHiddenFeatures: true);
        var rule = new RuleDraft(PartitionRule.ForSample("r0", 0, new ItemKey("item.poison")), sample);

        var heat = rule.AddCondition("heat");
        var types = rule.AddCondition("type");
        var legality = rule.AddCondition("legality");
        var stolen = rule.AddCondition("stolen");

        Assert.That((heat.Operator, heat.Target), Is.EqualTo((PartitionOperator.Eq, PartitionValue.Of(40))));
        Assert.That((types.Operator, types.Target), Is.EqualTo((PartitionOperator.Contains, (PartitionValue?)null)),
            "A sample with multiple types leaves the target unset.");
        Assert.That((legality.Operator, legality.Target), Is.EqualTo((PartitionOperator.Is, PartitionValue.Of("high"))));
        Assert.That((stolen.Operator, stolen.Target), Is.EqualTo((PartitionOperator.Is, (PartitionValue?)null)));
        Assert.That(rule.Validate(), Is.EqualTo(PartitionIssue.MissingValue));
    }

    [Test]
    public void RuleDraft_NeverFillsAnUnknownValueWithZero()
    {
        var rule = new RuleDraft(PartitionRule.ForSample("r0", 0, Apple), ItemAttributeSet.Unreadable);

        Assert.That(rule.AddCondition("heat").Target, Is.Null);
    }

    [Test]
    public void RuleDraft_ChangingTheFieldResetsTheRowAndRangesManageTheUpperBound()
    {
        var rule = new RuleDraft(PartitionRule.ForSample("r0", 0, Apple), null);
        rule.AddCondition("heat");
        rule.SetOperator(1, PartitionOperator.Range);
        rule.SetTarget(1, PartitionValue.Of(10));
        rule.SetUpper(1, PartitionValue.Of(20));
        Assert.That(rule.Validate(), Is.Null);

        rule.SetOperator(1, PartitionOperator.Lt);
        Assert.That(rule.Rows[1].Upper, Is.Null);
        Assert.Throws<InvalidOperationException>(new Action(() => rule.SetUpper(1, PartitionValue.Of(1))));
        Assert.Throws<ArgumentException>(new Action(() => rule.SetOperator(1, PartitionOperator.Contains)));

        rule.ChangeField(1, "legality");
        Assert.That((rule.Rows[1].Field.Id, rule.Rows[1].Operator, rule.Rows[1].Target),
            Is.EqualTo(("legality", PartitionOperator.Is, (PartitionValue?)null)));
    }

    [Test]
    public void RuleDraft_CompleteReturnsTheEditedRuleAndRefusesInvalidOnes()
    {
        var original = PartitionRule.ForSample("r0", 4, Apple);
        var rule = new RuleDraft(original, null) { Name = "  apples  " };
        rule.AddCondition("stolen");
        rule.SetOperator(1, PartitionOperator.IsNot);

        var done = rule.Complete();

        Assert.That((done.RuleId, done.Slot, done.Name, done.Conditions.Count), Is.EqualTo(("r0", 4, "apples", 2)));
        Assert.That(original.Conditions.Count, Is.EqualTo(1), "The stored rule is untouched until the overview applies.");

        rule.Remove(1);
        rule.Remove(0);
        Assert.That(rule.Validate(), Is.EqualTo(PartitionIssue.NoConditions));
        Assert.Throws<InvalidOperationException>(new Action(() => rule.Complete()));
    }

    [Test]
    public void RuleDraft_LintsAContradiction()
    {
        var rule = new RuleDraft(PartitionRule.ForSample("r0", 0, Apple), null);
        rule.AddCondition("stolen");
        rule.AddCondition("stolen");
        rule.SetOperator(2, PartitionOperator.IsNot);

        Assert.That(rule.Lint().Single().Kind, Is.EqualTo(PartitionWarningKind.NeverMatches));
    }

    [Test]
    public void Summary_JoinsLocalizedConditions()
    {
        var rule = new PartitionRule("r0", 0, null, null, new[]
        {
            new PartitionCondition("item", PartitionOperator.Is, PartitionValue.Of("item.poison")),
            new PartitionCondition("heat", PartitionOperator.Lt, PartitionValue.Of(40))
        });

        var text = RuleSummary.Describe(rule,
            (key, args) => key switch
            {
                "partition_field_item" => "Item",
                "partition_field_heat" => "Heat",
                "partition_summary_is" => $"{args[0]} is {args[1]}",
                "partition_summary_lt" => $"{args[0]} below {args[1]}%",
                "partition_summary_and" => ", and ",
                _ => key
            },
            (field, value) => field.Id == "item" ? "nightmare dust" : value.ToString());

        Assert.That(text, Is.EqualTo("Item is nightmare dust, and Heat below 40%"));
    }

    [Test]
    public void Session_DropsCallbacksAfterItEndsOrForAnotherTarget()
    {
        var cell = new object();
        var session = new PartitionSession(cell);

        Assert.That(session.Accepts(cell), Is.True);
        Assert.That(session.Accepts(new object()), Is.False);
        session.End();
        Assert.That(session.Accepts(cell), Is.False);
    }

    [Test]
    public void Gestures_SeparateClicksDoubleClicksAndDragsAndReportEachReleaseOnce()
    {
        var arbiter = new GridGestureArbiter(0.3);

        arbiter.Press(2);
        Assert.That(arbiter.Release(2, 1.0), Is.EqualTo(GridGesture.Click));
        Assert.That(arbiter.Release(2, 1.0), Is.EqualTo(GridGesture.None), "A release is reported once.");
        arbiter.Press(2);
        Assert.That(arbiter.Release(2, 1.2), Is.EqualTo(GridGesture.DoubleClick));

        arbiter.Press(2);
        arbiter.Move(5);
        Assert.That(arbiter.IsDragging, Is.True);
        Assert.That(arbiter.Release(5, 1.3), Is.EqualTo(GridGesture.Drop));
        arbiter.Press(5);
        Assert.That(arbiter.Release(5, 1.35), Is.EqualTo(GridGesture.Click), "A drag never counts toward a double-click.");

        arbiter.Press(1);
        arbiter.Move(3);
        arbiter.Cancel();
        Assert.That(arbiter.Release(3, 2.0), Is.EqualTo(GridGesture.None));
        arbiter.Press(4);
        Assert.That(arbiter.Release(4, 3.0), Is.EqualTo(GridGesture.Click));
        arbiter.Press(4);
        Assert.That(arbiter.Release(4, 3.5), Is.EqualTo(GridGesture.Click), "Too slow for a double-click.");
    }
}
