using System;
using System.Linq;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class PartitionEditorTests
{
    [Test]
    public void BeginAddRemoveAndApply_ReturnsPureWhitelistWithoutNativeMutation()
    {
        var editor = new PartitionEditor();
        var apple = new ItemKey("item.apple");
        var pear = new ItemKey("item.pear");
        editor.Begin(CellPartition.FromUserSubmission(new[] { apple }));

        Assert.That(editor.Add(pear), Is.True);
        Assert.That(editor.Remove(apple), Is.True);
        var applied = editor.Apply();

        Assert.That(applied.Mode, Is.EqualTo(CellPartitionMode.Whitelist));
        Assert.That(applied.Keys, Is.EqualTo(new[] { pear }));
        Assert.That(editor.IsEditing, Is.False);
        Assert.That(editor.IsDirty, Is.False);
    }

    [Test]
    public void ClearAndApply_UsesExplicitUnfilteredEmptyEdit()
    {
        var editor = new PartitionEditor();
        editor.Begin(CellPartition.FromUserSubmission(new[] { new ItemKey("item.apple") }));

        Assert.That(editor.Clear(), Is.True);
        var applied = editor.Apply();

        Assert.That(applied, Is.SameAs(CellPartition.Unfiltered));
        Assert.That(applied.Matches(new ItemKey("item.any")), Is.True);
    }

    [Test]
    public void Cancel_RestoresBaselineAndDoesNotChangeTheOriginalPartition()
    {
        var original = CellPartition.FromUserSubmission(new[] { new ItemKey("item.apple") });
        var editor = new PartitionEditor();
        editor.Begin(original);
        editor.Add(new ItemKey("item.pear"));
        editor.Cancel();

        Assert.That(original.Keys.Select(key => key.Identifier), Is.EqualTo(new[] { "item.apple" }));
        Assert.That(editor.IsEditing, Is.False);
        Assert.That(editor.DraftKeys, Is.EqualTo(new[] { new ItemKey("item.apple") }));
    }

    [Test]
    public void TryApply_Accepted_ClosesDraftAndReturnsDetachedCandidate()
    {
        var editor = new PartitionEditor();
        var apple = new ItemKey("item.apple");
        var pear = new ItemKey("item.pear");
        editor.Begin(CellPartition.FromUserSubmission(new[] { apple }));
        editor.Add(pear);
        CellPartition? observed = null;
        Assert.That(editor.TryApply(candidate => { observed = candidate; return true; }, out var applied), Is.True);
        Assert.That(applied, Is.SameAs(observed));
        Assert.That(applied!.Keys, Is.EqualTo(new[] { apple, pear }));
        Assert.That(editor.IsEditing, Is.False);
        Assert.That(editor.IsDirty, Is.False);
        editor.Begin(CellPartition.Unfiltered);
        editor.Add(new ItemKey("item.other"));
        Assert.That(applied.Keys, Is.EqualTo(new[] { apple, pear }));
    }

    [Test]
    public void TryApply_Rejected_LeavesDraftAndBaselineForCancel()
    {
        var apple = new ItemKey("item.apple"); var pear = new ItemKey("item.pear");
        var editor = new PartitionEditor(); editor.Begin(CellPartition.FromUserSubmission(new[] { apple })); editor.Add(pear);
        Assert.That(editor.TryApply(_ => false, out var applied), Is.False);
        Assert.That(applied, Is.Null);
        Assert.That(editor.IsEditing, Is.True);
        Assert.That(editor.IsDirty, Is.True);
        Assert.That(editor.DraftKeys, Is.EqualTo(new[] { apple, pear }));
        editor.Cancel();
        Assert.That(editor.DraftKeys, Is.EqualTo(new[] { apple }));
        Assert.That(editor.IsEditing, Is.False);
        Assert.That(editor.IsDirty, Is.False);
    }

    [Test]
    public void TryApply_Throw_RetainsDraftAndAllowsRetry()
    {
        var editor = new PartitionEditor(); editor.Begin(CellPartition.Unfiltered);
        var key = new ItemKey("item.apple"); editor.Add(key);
        Assert.Throws<InvalidOperationException>((Action)(() => editor.TryApply(_ => throw new InvalidOperationException("failure"), out _)));
        Assert.That(editor.IsEditing, Is.True);
        Assert.That(editor.IsDirty, Is.True);
        Assert.That(editor.DraftKeys, Is.EqualTo(new[] { key }));
        Assert.That(editor.TryApply(_ => true, out var applied), Is.True);
        Assert.That(applied!.Keys, Is.EqualTo(new[] { key }));
        Assert.That(editor.IsEditing, Is.False);
    }

    [TestCase("begin")]
    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("clear")]
    [TestCase("cancel")]
    [TestCase("apply")]
    [TestCase("submit")]
    public void TryApply_ReentrantOperations_FailWithoutChangingDraft(string operation)
    {
        var apple = new ItemKey("item.apple"); var pear = new ItemKey("item.pear");
        var editor = new PartitionEditor(); editor.Begin(CellPartition.FromUserSubmission(new[] { apple })); editor.Add(pear);
        Assert.That(editor.TryApply(candidate =>
        {
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                switch (operation)
                {
                    case "begin": editor.Begin(CellPartition.Unfiltered); break;
                    case "add": editor.Add(new ItemKey("item.other")); break;
                    case "remove": editor.Remove(apple); break;
                    case "clear": editor.Clear(); break;
                    case "cancel": editor.Cancel(); break;
                    case "apply": editor.Apply(); break;
                    case "submit": editor.TryApply(_ => true, out _); break;
                }
            }));
            Assert.That(candidate.Keys, Is.EqualTo(new[] { apple, pear }));
            return false;
        }, out var applied), Is.False);
        Assert.That(applied, Is.Null);
        Assert.That(editor.IsEditing, Is.True);
        Assert.That(editor.IsDirty, Is.True);
        Assert.That(editor.DraftKeys, Is.EqualTo(new[] { apple, pear }));
        editor.Cancel();
        Assert.That(editor.DraftKeys, Is.EqualTo(new[] { apple }));
    }

    [Test]
    public void DuplicateAdd_IsNoOpAndSixtyFourKeyDraftIsRejected()
    {
        var editor = new PartitionEditor();
        var first = new ItemKey("item.first");
        editor.Begin(CellPartition.FromUserSubmission(new[] { first }));

        Assert.That(editor.Add(first), Is.False);
        for (var index = 1; index < 63; index++)
        {
            Assert.That(editor.Add(new ItemKey($"item.{index}")), Is.True);
        }

        Assert.Throws<InvalidOperationException>(new Action(() => editor.Add(new ItemKey("item.overflow"))));
    }

    [Test]
    public void OperationsWithoutDraftFailClosed()
    {
        var editor = new PartitionEditor();
        Assert.Throws<InvalidOperationException>(new Action(() => editor.Clear()));
        Assert.Throws<InvalidOperationException>(new Action(() => editor.Apply()));
        Assert.Throws<InvalidOperationException>(new Action(editor.Cancel));
    }
}
