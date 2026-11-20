using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Compat.BrewingExpansion;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class BrewingTooltipCompatibilityTests
{
    [TestCase("3.6.8.5", false)]
    [TestCase("3.6.8.6", true)]
    [TestCase("3.6.8.7", true)]
    [TestCase("3.6.8.10", true)]
    [TestCase("3.6.9", true)]
    [TestCase("3.7.0", false)]
    [TestCase("4.0.0", false)]
    [TestCase("3.6", false)]
    [TestCase("3.6.9-beta", false)]
    [TestCase("3.6.9.0.0", false)]
    [TestCase(" 3.6.9", false)]
    [TestCase("", false)]
    public void Supports_UsesNumericBounds(string version, bool supported) =>
        Assert.That(BrewingExpansionVersions.Supports(version), Is.EqualTo(supported));

    [TestCase(nameof(TooltipSignatures.Expected), true)]
    [TestCase(nameof(TooltipSignatures.WrongReturn), false)]
    [TestCase(nameof(TooltipSignatures.WrongItem), false)]
    [TestCase(nameof(TooltipSignatures.Instance), false)]
    [TestCase(nameof(TooltipSignatures.Generic), false)]
    [TestCase("Missing", false)]
    public void Target_RequiresAnExactSignature(string name, bool expected)
    {
        var method = typeof(TooltipSignatures).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.That(BrewingTooltipTarget.HasExpectedSignature(method), Is.EqualTo(expected));
    }

    [Test]
    public void Target_RequiresDirectCallsToBothSetters()
    {
        var description = typeof(GameItem).GetProperty(nameof(GameItem.shortDescription))!.SetMethod!;
        var flavor = typeof(GameItem).GetProperty(nameof(GameItem.flavorText))!.SetMethod!;
        var unrelated = new KeyValuePair<OpCode, object>(OpCodes.Ldftn, flavor);
        var writeDescription = new KeyValuePair<OpCode, object>(OpCodes.Callvirt, description);
        var writeFlavor = new KeyValuePair<OpCode, object>(OpCodes.Call, flavor);

        Assert.That(BrewingTooltipTarget.HasLoreWrites(new[] { writeDescription, unrelated }, description, flavor), Is.False);
        Assert.That(BrewingTooltipTarget.HasLoreWrites(new[] { writeDescription, writeFlavor }, description, flavor), Is.True);
    }

    [Test]
    public void Render_ReturnsNativeTextAndRestoresBothFieldsWithoutRefreshing()
    {
        var item = CreateItem();
        var result = TerminalTooltipScope.Render(item, () =>
        {
            Change(item, "Short\ndescription", "Flavor\ntext");
            return (item.shortDescription, item.flavorText);
        }, NeverRefresh);

        Assert.That(result, Is.EqualTo(("Short\ndescription", "Flavor\ntext")));
        Assert.That(item.shortDescription, Is.EqualTo("Short description"));
        Assert.That(item.flavorText, Is.EqualTo("Flavor text"));
        Assert.That(TerminalTooltipScope.For(item), Is.Null);
    }

    [Test]
    public void Render_PreservesAnotherWritersValueAndRequestsOneRefresh()
    {
        var item = CreateItem();
        var refreshed = 0;
        TerminalTooltipScope.Render(item, () =>
        {
            Change(item, "Wrapped A", "Wrapped flavor");
            item.shortDescription = "Different text from another mod";
            Change(item, "Wrapped B", "Wrapped flavor again");
            item.flavorText = "Different flavor from another mod";
            return true;
        }, () => refreshed++);

        Assert.That(item.shortDescription, Is.EqualTo("Different text from another mod"));
        Assert.That(item.flavorText, Is.EqualTo("Different flavor from another mod"));
        Assert.That(refreshed, Is.EqualTo(1));
    }

    [Test]
    public void Render_NestedScopesForSameReferenceRestoreOnlyAfterOutermostCall()
    {
        var parent = CreateItem();
        var other = CreateItem();
        TerminalTooltipScope.Render(parent, () =>
        {
            Change(parent, "Wrapped once", parent.flavorText);
            TerminalTooltipScope.Render(other, () =>
            {
                Assert.That(TerminalTooltipScope.For(parent), Is.Null);
                TerminalTooltipScope.Render(parent, () =>
                {
                    Change(parent, "Wrapped twice", parent.flavorText);
                    return true;
                }, NeverRefresh);
                Change(other, "Wrapped other", other.flavorText);
                return true;
            }, NeverRefresh);
            Assert.That(parent.shortDescription, Is.EqualTo("Wrapped twice"));
            Assert.That(other.shortDescription, Is.EqualTo("Short description"));
            return true;
        }, NeverRefresh);
        Assert.That(parent.shortDescription, Is.EqualTo("Short description"));
    }

    [Test]
    public void Render_OnErrorRestoresTextAndPreservesOriginalException()
    {
        var item = CreateItem();
        var error = new InvalidOperationException();
        Action render = () =>
        {
            TerminalTooltipScope.Render<bool>(item, () =>
            {
                Change(item, "Wrapped", item.flavorText);
                throw error;
            }, NeverRefresh);
        };
        var observed = Assert.Throws<InvalidOperationException>(render);

        Assert.That(observed, Is.SameAs(error));
        Assert.That(item.shortDescription, Is.EqualTo("Short description"));
        Assert.That(TerminalTooltipScope.For(item), Is.Null);
    }

    [Test]
    public void Render_NoMatchingCallbackLeavesTextAlone()
    {
        var item = CreateItem();
        var other = CreateItem();
        Assert.That(TerminalTooltipScope.For(item), Is.Null);
        TerminalTooltipScope.Render(item, () =>
        {
            Assert.That(TerminalTooltipScope.For(other), Is.Null);
            item.shortDescription = "Updated by another mod";
            return true;
        }, NeverRefresh);
        Assert.That(item.shortDescription, Is.EqualTo("Updated by another mod"));
    }

    private sealed class TooltipSignatures
    {
        internal static void Expected(RichTextBuilder builder, GameItem item) { }
        internal static bool WrongReturn(RichTextBuilder builder, GameItem item) => false;
        internal static void WrongItem(RichTextBuilder builder, object item) { }
        internal void Instance(RichTextBuilder builder, GameItem item) { }
        internal static void Generic<T>(RichTextBuilder builder, GameItem item) { }
    }

    private static GameItem CreateItem() => new("test_fruit")
    {
        shortDescription = "Short description",
        flavorText = "Flavor text"
    };

    private static void Change(GameItem item, string description, string flavor)
    {
        var before = (item.shortDescription, item.flavorText);
        item.shortDescription = description;
        item.flavorText = flavor;
        TerminalTooltipScope.For(item)!.Record(before.shortDescription, before.flavorText);
    }

    private static void NeverRefresh() => Assert.Fail("This tooltip should not request a storage refresh.");
}
