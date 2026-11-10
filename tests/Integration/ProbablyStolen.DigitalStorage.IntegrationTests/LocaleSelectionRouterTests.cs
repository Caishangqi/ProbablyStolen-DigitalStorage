using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class LocaleSelectionRouterTests
{
    [TestCase(null, false)]
    [TestCase("zh", true)]
    [TestCase("ZH-hans", true)]
    [TestCase("zh_CN", true)]
    [TestCase("zh-", false)]
    [TestCase("zho", false)]
    [TestCase("en", false)]
    public void AutoRequiresConfirmedChinese(string? locale, bool expected)
    {
        Assert.That(PinyinSearchModePolicy.IsEnabled(PinyinSearchMode.Auto, locale), Is.EqualTo(expected));
        Assert.That(PinyinSearchModePolicy.IsEnabled((PinyinSearchMode)99, locale), Is.EqualTo(expected));
        Assert.That(PinyinSearchModePolicy.IsEnabled(PinyinSearchMode.On, locale), Is.True);
        Assert.That(PinyinSearchModePolicy.IsEnabled(PinyinSearchMode.Off, locale), Is.False);
    }

    [Test]
    public void ChoicesPreserveCanonicalIndexOrder()
    {
        Assert.That(PinyinSearchModePolicy.HasCanonicalChoices(new[] { "Auto", "On", "Off" }), Is.True);
        Assert.That(PinyinSearchModePolicy.HasCanonicalChoices(new[] { "On", "Auto", "Off" }), Is.False);
        Assert.That(PinyinSearchModePolicy.IsValidIndex(-1), Is.False);
        Assert.That(PinyinSearchModePolicy.IsValidIndex(0), Is.True);
        Assert.That(PinyinSearchModePolicy.IsValidIndex(2), Is.True);
        Assert.That(PinyinSearchModePolicy.IsValidIndex(3), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OverlappingRequestsSettleOnlyAfterBothComplete(bool reverse)
    {
        var events = new List<string?>();
        var current = "zh";
        using var router = new LocaleSelectionRouter(() => current, events.Add, _ => { });
        var a = router.Wrap(new Fake());
        var b = router.Wrap(new Fake());
        Assert.That(events, Is.Empty);
        Assert.That(a.MoveNext(), Is.True);
        Assert.That(b.MoveNext(), Is.True);
        var first = reverse ? b : a;
        var last = reverse ? a : b;
        Assert.That(first.MoveNext(), Is.False);
        Assert.That(events, Is.EqualTo(new string?[] { null, null }));
        current = "en";
        Assert.That(last.MoveNext(), Is.False);
        Assert.That(last.MoveNext(), Is.False);
        Assert.That(events, Is.EqualTo(new string?[] { null, null, "en" }));
    }

    [Test]
    public void CancellationAndFailuresDoNotGuessInitialLocale()
    {
        var events = new List<string?>();
        var reads = 0;
        using var router = new LocaleSelectionRouter(() => { reads++; return "zh"; }, events.Add, _ => { });
        var neverStarted = (IDisposable)router.Wrap(new Fake());
        neverStarted.Dispose();
        var cancelled = router.Wrap(new Fake());
        cancelled.MoveNext();
        ((IDisposable)cancelled).Dispose();
        var failed = router.Wrap(new Fake { FailMove = true, FailDispose = true });
        Assert.That((Action)(() => { failed.MoveNext(); }), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("move"));
        Assert.That(reads, Is.Zero);
        Assert.That(events, Is.EqualTo(new string?[] { null, null }));
    }

    [Test]
    public void CurrentErrorReleasesAndOriginalExceptionSurvivesDisposeError()
    {
        var events = new List<string?>();
        using var router = new LocaleSelectionRouter(() => "en", events.Add, _ => { });
        var good = router.Wrap(new Fake());
        good.MoveNext();
        var broken = router.Wrap(new Fake { FailCurrent = true, FailDispose = true });
        broken.MoveNext();
        good.MoveNext();
        Assert.That((Action)(() => { _ = broken.Current; }), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("current"));
        Assert.That(events, Is.EqualTo(new string?[] { null, null, "en" }));
    }

    [Test]
    public void DisposeFailureReleasesActiveAndACompletedRequestAllowsCurrentRead()
    {
        var events = new List<string?>();
        using var router = new LocaleSelectionRouter(() => "zh-CN", events.Add, _ => { });
        var completed = router.Wrap(new Fake());
        var failing = router.Wrap(new Fake { FailDispose = true });
        Assert.That(completed.MoveNext(), Is.True);
        Assert.That(completed.Current, Is.EqualTo("yield"));
        Assert.That(failing.MoveNext(), Is.True);
        Assert.That(completed.MoveNext(), Is.False);
        Assert.That((Action)(() => ((IDisposable)failing).Dispose()), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("dispose"));
        Assert.That(events, Is.EqualTo(new string?[] { null, null, "zh-CN" }));
    }

    [Test]
    public void DisposedOwnerCannotNotifyAndReaderFailureIsUnknown()
    {
        var events = new List<string?>();
        var old = new LocaleSelectionRouter(() => "zh", events.Add, _ => { });
        var stale = old.Wrap(new Fake());
        old.Dispose();
        stale.MoveNext();
        stale.MoveNext();
        Assert.That(events, Is.Empty);
        using var router = new LocaleSelectionRouter(() => throw new InvalidOperationException(), events.Add, _ => { });
        var active = router.Wrap(new Fake());
        active.MoveNext();
        active.MoveNext();
        Assert.That(events, Is.EqualTo(new string?[] { null, null }));
    }

    private sealed class Fake : IEnumerator, IDisposable
    {
        private bool _yielded;
        public bool FailMove { get; set; }
        public bool FailCurrent { get; set; }
        public bool FailDispose { get; set; }
        public object Current => FailCurrent ? throw new InvalidOperationException("current") : "yield";
        public bool MoveNext()
        {
            if (FailMove) throw new InvalidOperationException("move");
            if (_yielded) return false;
            _yielded = true;
            return true;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose()
        {
            if (FailDispose) throw new InvalidOperationException("dispose");
        }
    }
}
