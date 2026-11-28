using System;
using System.Text;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Api.Core.Tests;

[TestFixture]
public sealed class Wtf8Tests
{
    [TestCase("")]
    [TestCase("coffee_bean")]
    [TestCase("咖啡豆 é")]
    [TestCase("\U0001F600")]
    public void WellFormedText_MatchesUtf8(string text)
    {
        Assert.That(Wtf8.GetBytes(text), Is.EqualTo(Encoding.UTF8.GetBytes(text)));
        Assert.That(Wtf8.GetByteCount(text, out var lone), Is.EqualTo(Encoding.UTF8.GetByteCount(text)));
        Assert.That(lone, Is.False);
    }

    // Attribute arguments are stored as UTF-8 and would replace lone surrogates, so the cases are built in code.
    private static readonly string[] LoneSurrogateTexts = { "\ud800", "a\udc00b", "\udc00\ud800", "\ud800\ud800\udc00" };

    [TestCaseSource(nameof(LoneSurrogateTexts))]
    public void LoneSurrogates_RoundTrip(string text)
    {
        var bytes = Wtf8.GetBytes(text);
        Assert.That(Wtf8.GetByteCount(text, out var lone), Is.EqualTo(bytes.Length));
        Assert.That(lone, Is.True);
        Assert.That(Wtf8.GetString(bytes, 0, bytes.Length), Is.EqualTo(text));
    }

    [Test]
    public void GetString_ReadsWithinOffsetAndLength()
    {
        var inner = Wtf8.GetBytes("x\ud800y");
        var data = new byte[inner.Length + 4];
        data[0] = 0xFF;
        data[1] = 0xFF;
        Array.Copy(inner, 0, data, 2, inner.Length);
        data[^1] = 0xFF;
        data[^2] = 0xFF;
        Assert.That(Wtf8.GetString(data, 2, inner.Length), Is.EqualTo("x\ud800y"));
        Assert.That(Wtf8.GetString(data, 2, inner.Length + 1), Is.Null);
    }

    [Test]
    public void RandomText_RoundTripsAndEncodesOnce()
    {
        var random = new Random(20261127);
        var units = new[] { 'a', '\u00e9', '\u4e2d', '\ud800', '\udbff', '\udc00', '\udfff', '\uffff' };
        for (var run = 0; run < 2000; run++)
        {
            var chars = new char[random.Next(0, 12)];
            for (var i = 0; i < chars.Length; i++) chars[i] = units[random.Next(units.Length)];
            var text = new string(chars);
            var bytes = Wtf8.GetBytes(text);
            var decoded = Wtf8.GetString(bytes, 0, bytes.Length);
            Assert.That(decoded, Is.EqualTo(text));
            Assert.That(Wtf8.GetBytes(decoded!), Is.EqualTo(bytes));
        }
    }

    [Test]
    public void NonCanonicalBytes_AreRefused()
    {
        var splitPair = new byte[] { 0xED, 0xA0, 0x80, 0xED, 0xB0, 0x80 };
        var overlong = new byte[] { 0xC0, 0xAF };
        var overlongThree = new byte[] { 0xE0, 0x80, 0xAF };
        var tooLarge = new byte[] { 0xF4, 0x90, 0x80, 0x80 };
        var truncated = new byte[] { 0xE4, 0xB8 };
        foreach (var bytes in new[] { splitPair, overlong, overlongThree, tooLarge, truncated })
            Assert.That(Wtf8.GetString(bytes, 0, bytes.Length), Is.Null);
    }
}
