using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

public sealed class PictureSourceChainTests
{
    [Test]
    public void Find_ReturnsFirstSourceThatHasASample()
    {
        var key = new ItemKey("apple");
        var calls = 0;
        var chain = new PictureSourceChain<string>(_ => { calls++; return null; }, _ => "cell page", _ => "never");

        Assert.That(chain.Find(key), Is.EqualTo("cell page"));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Find_ReturnsNullWhenNoSourceHasASample()
    {
        var chain = new PictureSourceChain<string>(_ => null, _ => null);

        Assert.That(chain.Find(new ItemKey("apple")), Is.Null);
    }

    [Test]
    public void Find_PropagatesSourceFailureSoTheCallerFallsBack()
    {
        var chain = new PictureSourceChain<string>(_ => throw new InvalidOperationException("synthetic"), _ => "later");

        Assert.Throws<InvalidOperationException>(new Action(() => { chain.Find(new ItemKey("apple")); }));
    }

    [Test]
    public void Constructor_RejectsMissingSources()
    {
        Assert.Throws<ArgumentException>(new Action(() => { _ = new PictureSourceChain<string>(); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new PictureSourceChain<string>(_ => null, null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { new PictureSourceChain<string>(_ => null).Find(null!); }));
    }

    [TestCase(true, true, true, PictureSource.Layers)]
    [TestCase(true, false, false, PictureSource.Layers)]
    [TestCase(false, true, true, PictureSource.Sprite)]
    [TestCase(false, false, true, PictureSource.Placeholder)]
    [TestCase(false, false, false, PictureSource.None)]
    public void Choose_PrefersLayersThenSpriteThenPlaceholder(bool layers, bool sprite, bool placeholder,
        PictureSource expected)
    {
        Assert.That(PictureSourceChain<string>.Choose(layers, sprite, placeholder), Is.EqualTo(expected));
    }
}
