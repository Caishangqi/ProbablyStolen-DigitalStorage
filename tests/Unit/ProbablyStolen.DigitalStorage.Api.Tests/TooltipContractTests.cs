using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;

namespace ProbablyStolen.DigitalStorage.Api.Tests;

[TestFixture]
public sealed class TooltipContractTests
{
    [Test]
    public void TooltipStyle_RejectsColorsOutside24BitRange()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>((Action)(() => new TooltipStyle(TooltipStyleKind.Rgb, 0x1000000, null, null)));
        Assert.That(TooltipStyle.Native.Kind, Is.EqualTo(TooltipStyleKind.Native));
    }
}
