using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class CustomerIdentityTests
{
    [TestCase("digital_storage:data_scientist", "digital_storage.data_scientist")]
    [TestCase("test_mod:courier/intro", "test_mod.courier/intro")]
    public void ToNativeId_JoinsNamespaceAndPathWithADot(string id, string expected)
    {
        Assert.That(CustomerIdentity.ToNativeId(Identifier.Parse(id)), Is.EqualTo(expected));
    }

    [Test]
    public void ToNativeId_RejectsTheDefaultIdentifier()
    {
        Assert.Throws<ArgumentException>((Action)(() => CustomerIdentity.ToNativeId(default)));
    }

    [TestCase("digital_storage:digital_scientist", true)]
    [TestCase("other_mod:Portrait Sheet", true)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("digital_scientist", false)]
    [TestCase(":digital_scientist", false)]
    [TestCase("digital_storage:", false)]
    [TestCase("a:b:c", false)]
    [TestCase(" digital_storage:digital_scientist", false)]
    public void IsPortraitKey_AcceptsOnlyModSpriteKeys(string? key, bool expected)
    {
        Assert.That(CustomerIdentity.IsPortraitKey(key), Is.EqualTo(expected));
    }
}
