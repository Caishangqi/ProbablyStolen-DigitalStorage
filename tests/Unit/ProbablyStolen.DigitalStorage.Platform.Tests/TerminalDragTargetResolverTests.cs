using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
internal sealed class TerminalDragTargetResolverTests
{
    [Test]
    public void HitClassification_PreservesNearestNativeElementType()
    {
        var slot = (PixelElement)RuntimeHelpers.GetUninitializedObject(typeof(GameSlotInventory));
        var item = (PixelElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        var window = (PixelElement)RuntimeHelpers.GetUninitializedObject(typeof(PixelWindow));

        Assert.Multiple((Action)(() =>
        {
            Assert.That(TerminalDragTargetResolver.ClassifyHit(slot), Is.EqualTo(TerminalDropHitKind.Inventory));
            Assert.That(TerminalDragTargetResolver.ClassifyHit(item), Is.EqualTo(TerminalDropHitKind.Item));
            Assert.That(TerminalDragTargetResolver.ClassifyHit(window), Is.EqualTo(TerminalDropHitKind.Window));
            Assert.That(TerminalDragTargetResolver.ClassifyHit(null), Is.EqualTo(TerminalDropHitKind.Unsupported));
        }));
    }

    [Test]
    public void Constructor_NullWindow_IsRejectedWithoutCreatingUnityObjects()
    {
        Assert.Throws<ArgumentNullException>((Action)(() => new TerminalDragTargetResolver(null!, () => true)));
    }

    [Test]
    public void Constructor_NullSession_IsRejectedWithoutCreatingUnityObjects()
    {
        var window = OpaqueWindow();
        Assert.Throws<ArgumentNullException>((Action)(() => new TerminalDragTargetResolver(window, null!)));
    }

    [Test]
    public void InvalidPointer_ReturnsRejectedWithoutSessionOrRaycast()
    {
        var sessionCalls = 0;
        var resolver = new TerminalDragTargetResolver(OpaqueWindow(), () =>
        {
            sessionCalls++;
            return true;
        });

        var resolved = resolver.TryResolveCandidate(new PointerPoint(float.NaN, 1), out var target, out var state);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(target, Is.Null);
            Assert.That(state, Is.EqualTo(TerminalDragTargetState.Rejected));
            Assert.That(sessionCalls, Is.Zero);
        }));
    }

    [Test]
    public void InfinitePointer_ReturnsRejectedWithoutSessionOrRaycast()
    {
        var sessionCalls = 0;
        var resolver = new TerminalDragTargetResolver(OpaqueWindow(), () =>
        {
            sessionCalls++;
            return true;
        });

        var resolved = resolver.TryResolveCandidate(new PointerPoint(float.PositiveInfinity, 1), out var target,
            out var state);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(target, Is.Null);
            Assert.That(state, Is.EqualTo(TerminalDragTargetState.Rejected));
            Assert.That(sessionCalls, Is.Zero);
        }));
    }

    private static CustomUIWindow OpaqueWindow() =>
        (CustomUIWindow)RuntimeHelpers.GetUninitializedObject(typeof(CustomUIWindow));
}
