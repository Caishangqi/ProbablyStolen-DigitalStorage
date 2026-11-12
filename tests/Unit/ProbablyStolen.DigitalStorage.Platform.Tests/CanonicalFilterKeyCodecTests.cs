using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
public sealed class CanonicalFilterKeyCodecTests
{
    [Test]
    public void EncodeDecode_CanonicalIdentity_RoundTrips()
    {
        var input = new CanonicalIdentityInput(
            "item.apple",
            Array.Empty<string>(),
            Array.Empty<CanonicalIdentityTag>(),
            Array.Empty<CanonicalIdentityFeature>());
        var key = CanonicalIdentityEncoder.Encode(input);

        var encoded = CanonicalFilterKeyCodec.Encode(key);
        var decoded = CanonicalFilterKeyCodec.Decode(encoded);

        Assert.That(decoded, Is.EqualTo(key));
        Assert.That(decoded.IdentityVersion, Is.EqualTo(1));
    }

    [Test]
    public void Encode_LegacyIdentity_IsRejected()
    {
        Assert.Throws<ArgumentException>((Action)(() => CanonicalFilterKeyCodec.Encode(new ItemKey("item.apple"))));
    }

    [Test]
    public void Decode_InvalidText_IsRejected()
    {
        Assert.Throws<ArgumentException>((Action)(() => CanonicalFilterKeyCodec.Decode("not-base64")));
    }

    [Test]
    public void TryDecode_ReportsInvalidTextWithoutThrowing()
    {
        var key = CanonicalIdentityEncoder.Encode(new CanonicalIdentityInput(
            "item.apple", Array.Empty<string>(), Array.Empty<CanonicalIdentityTag>(),
            Array.Empty<CanonicalIdentityFeature>()));
        var encoded = CanonicalFilterKeyCodec.Encode(key);

        Assert.That(CanonicalFilterKeyCodec.TryDecode(encoded, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(key));
        Assert.That(encoded, Does.Not.Contain("\n"));
        Assert.That(CanonicalFilterKeyCodec.TryDecode("not base64!", out var invalid), Is.False);
        Assert.That(invalid, Is.Null);
    }
}
