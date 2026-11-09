using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class ScrollbarModelTests
{
    [Test]
    public void SetRange_RaisesMaximumBelowMinimumAndDisablesInput()
    {
        var model = new ScrollbarModel();
        model.SetRange(0, -3, 1);

        Assert.That(model.Max, Is.Zero);
        Assert.That(model.IsDisabled, Is.True);
        Assert.That(model.Wheel(-1f), Is.False);
        Assert.That(model.ClickTrack(above: false), Is.False);
        Assert.That(model.DragTo(50f, 0f, 100f, 20f), Is.False);
        Assert.That(model.GetHandleOffset(100f, 20f), Is.Zero);
        Assert.That(model.Current, Is.Zero);
    }

    [Test]
    public void SetRange_ClampsTheCurrentValueAndPageSize()
    {
        var model = new ScrollbarModel();
        model.SetRange(0, 10, 0);
        model.SetCurrent(8);

        model.SetRange(0, 4, 2);

        Assert.That(model.Current, Is.EqualTo(4));
        Assert.That(model.PageSize, Is.EqualTo(2));
        model.SetRange(0, 4, 0);
        Assert.That(model.PageSize, Is.EqualTo(1));
    }

    [TestCase(0, 0f)]
    [TestCase(2, 40f)]
    [TestCase(4, 80f)]
    public void HandleOffset_SpreadsTheFixedHandleOverTheFreeTrack(int current, float expected)
    {
        var model = new ScrollbarModel();
        model.SetRange(0, 4, 1);
        model.SetCurrent(current);

        // (current - min) * (trackHeight - handleHeight) / range
        Assert.That(model.GetHandleOffset(100f, 20f), Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void Wheel_MovesOnePageForAnyDeltaAndClamps()
    {
        var model = new ScrollbarModel();
        model.SetRange(0, 5, 2);

        Assert.That(model.Wheel(-3.5f), Is.True);
        Assert.That(model.Current, Is.EqualTo(2));
        Assert.That(model.Wheel(-0.1f), Is.True);
        Assert.That(model.Current, Is.EqualTo(4));
        Assert.That(model.Wheel(-1f), Is.True);
        Assert.That(model.Current, Is.EqualTo(5));
        Assert.That(model.Wheel(-1f), Is.False);
        Assert.That(model.Wheel(1f), Is.True);
        Assert.That(model.Current, Is.EqualTo(3));
        Assert.That(model.Wheel(0f), Is.False);
    }

    [Test]
    public void ClickTrack_PagesTowardTheClick()
    {
        var model = new ScrollbarModel();
        model.SetRange(0, 3, 1);

        Assert.That(model.ClickTrack(above: true), Is.False);
        Assert.That(model.ClickTrack(above: false), Is.True);
        Assert.That(model.Current, Is.EqualTo(1));
        Assert.That(model.ClickTrack(above: true), Is.True);
        Assert.That(model.Current, Is.Zero);
    }

    [TestCase(10f, 0)]
    [TestCase(29f, 1)]
    [TestCase(30f, 1)]
    [TestCase(50f, 2)]
    [TestCase(70f, 3)]
    [TestCase(500f, 4)]
    [TestCase(-50f, 0)]
    public void DragTo_RoundsTheClampedHandleFraction(float pointer, int expected)
    {
        var model = new ScrollbarModel();
        model.SetRange(0, 4, 1);
        model.SetCurrent(2);

        // Track 100, handle 20, grabbed 10 below its top: position = clamp01((pointer - 10) / 80) * 4.
        model.DragTo(pointer, 10f, 100f, 20f);

        Assert.That(model.Current, Is.EqualTo(expected));
    }
}
