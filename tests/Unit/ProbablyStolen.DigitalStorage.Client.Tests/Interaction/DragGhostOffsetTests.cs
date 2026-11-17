using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

namespace ProbablyStolen.DigitalStorage.Client.Tests.Interaction;

[TestFixture]
internal sealed class DragGhostOffsetTests
{
    [Test]
    public void At_PreservesGrabOffsetAndGapAcrossRows()
    {
        var origin = new PointerPoint(7, 8);
        var first = new DragGhostOffset(new PointerRectangle(0, 0, 10, 10), origin);
        var second = new DragGhostOffset(new PointerRectangle(24, -16, 34, -6), origin);
        var a = first.At(new PointerPoint(107, 208));
        var b = second.At(new PointerPoint(107, 208));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(a.X, Is.EqualTo(105));
            Assert.That(a.Y, Is.EqualTo(205));
            Assert.That(b.X - a.X, Is.EqualTo(24));
            Assert.That(b.Y - a.Y, Is.EqualTo(-16));
        }));
    }
}
