using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Workbench;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Workbench;

[TestFixture]
public sealed class PartitionHoverHintCoordinatorTests
{
    private HintView _view = null!;
    private PartitionHoverHintCoordinator _hints = null!;

    [SetUp]
    public void SetUp()
    {
        _view = new HintView();
        _view.Bound[3] = "rule-a: configure, copy, clear";
        _hints = new PartitionHoverHintCoordinator(_view.Show, _view.Hide);
    }

    [Test]
    public void StationaryHover_DoesNotHideOrReshowUnchangedText()
    {
        for (var frame = 0; frame < 20; frame++) _hints.Refresh(3, _view.Bound[3], canShow: true);

        Assert.That(_view.Shown, Has.Count.EqualTo(1));
        Assert.That(_view.Hides, Is.Zero);
        Assert.That(_view.VisibleText, Is.EqualTo(_view.Bound[3]));
    }

    [TestCase("rule-b: configure, copy, clear")]
    [TestCase("rule-a: configure, copy, clear")]
    public void StationaryPointer_AfterScrollRebind_RestoresTheCurrentBoundHint(string replacement)
    {
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        // FillGrid clears and rebinds the same button; no pointer-enter event is produced.
        _hints.Hide();
        _view.Bound[3] = replacement;
        _hints.Refresh(3, _view.Bound[3], canShow: true);

        Assert.That(_view.Shown, Has.Count.EqualTo(2));
        Assert.That(_view.VisibleText, Is.EqualTo(replacement));
    }

    [Test]
    public void StationaryPointer_AfterDragRelease_RestoresTheHint()
    {
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        _hints.Refresh(3, _view.Bound[3], canShow: false);
        Assert.That(_view.VisibleText, Is.Null, "a drag must hide the hint");

        _hints.Refresh(3, _view.Bound[3], canShow: true);

        Assert.That(_view.Shown, Has.Count.EqualTo(2));
        Assert.That(_view.VisibleText, Is.EqualTo(_view.Bound[3]));
    }

    [Test]
    public void StationaryPointer_AfterGestureCancellation_RestoresTheHint()
    {
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        _hints.Hide();
        Assert.That(_view.VisibleText, Is.Null);

        _hints.Refresh(3, _view.Bound[3], canShow: true);

        Assert.That(_view.Shown, Has.Count.EqualTo(2));
        Assert.That(_view.VisibleText, Is.EqualTo(_view.Bound[3]));
    }

    [Test]
    public void HoveringAnotherSlot_WithTheSameText_ChangesTheHintOwner()
    {
        _view.Bound[4] = _view.Bound[3];
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        _hints.Refresh(4, _view.Bound[4], canShow: true);

        Assert.That(_view.Shown, Is.EqualTo(new[] { 3, 4 }));
    }

    [Test]
    public void SuspendedOverview_HidesEvenAnUnobservedPointerEnter_AndRestoresOnResume()
    {
        // The engine's trigger can show a hint before the frame coordinator has observed it.
        _view.Show(3);
        _hints.Refresh(3, _view.Bound[3], canShow: false);
        Assert.That(_view.VisibleText, Is.Null);

        _hints.Refresh(3, _view.Bound[3], canShow: true);

        Assert.That(_view.Shown, Has.Count.EqualTo(2));
        Assert.That(_view.VisibleText, Is.EqualTo(_view.Bound[3]));
    }

    [TestCase(-1, null)]
    [TestCase(3, "")]
    [TestCase(3, null)]
    public void EmptyOrMissingSlot_HidesThePreviousHint(int slot, string? text)
    {
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        _hints.Refresh(slot, text, canShow: true);

        Assert.That(_view.VisibleText, Is.Null);
        Assert.That(_view.Shown, Has.Count.EqualTo(1));
    }

    [Test]
    public void FullCapacity_ReboundTextRemovesTheCopyHint_WithoutPointerReentry()
    {
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        _view.Bound[3] = "rule-a: configure, clear";
        _hints.Refresh(3, _view.Bound[3], canShow: true);
        _hints.Refresh(3, _view.Bound[3], canShow: true);

        Assert.That(_view.VisibleText, Is.EqualTo("rule-a: configure, clear"));
        Assert.That(_view.Shown, Has.Count.EqualTo(2));
    }

    private sealed class HintView
    {
        internal Dictionary<int, string> Bound { get; } = new();
        internal List<int> Shown { get; } = new();
        internal string? VisibleText { get; private set; }
        internal int Hides { get; private set; }

        internal void Show(int slot)
        {
            Shown.Add(slot);
            VisibleText = Bound[slot];
        }

        internal void Hide()
        {
            VisibleText = null;
            Hides++;
        }
    }
}
