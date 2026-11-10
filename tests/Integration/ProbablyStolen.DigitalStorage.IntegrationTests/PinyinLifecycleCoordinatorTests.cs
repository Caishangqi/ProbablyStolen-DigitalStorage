using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Platform.Composition;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class PinyinLifecycleCoordinatorTests
{
    [Test]
    public void PendingConfigApplyCannotLoadWhenAssetsBecomeReadyBeforeEnglishSettles()
    {
        var reads = 0;
        var revisions = 0;
        var current = new SearchConfiguration(enabled: false);
        using var gate = new DictionaryLoadGate(() => reads++);
        var coordinator = new PinyinLifecycleCoordinator(locale =>
            new SearchConfiguration(enabled: PinyinSearchModePolicy.IsEnabled(PinyinSearchMode.Auto, locale)),
            Apply, initiallyEnabled: current.PinyinEnabled);

        void Apply(SearchConfiguration configuration)
        {
            current = configuration;
            revisions++;
            gate.Update(configuration.PinyinEnabled);
        }

        coordinator.OnLocaleChanged("zh", settled: true);
        coordinator.OnLocaleChanged(null, settled: false);
        coordinator.OnConfigApplied();
        Assert.That(current.PinyinEnabled, Is.False);
        Assert.That(gate.MarkAssetsReady(), Is.False, "assets became ready while locale was pending");
        coordinator.OnLocaleChanged("en", settled: true);

        Assert.That(reads, Is.Zero);
        Assert.That(revisions, Is.EqualTo(4), "each locale/config transition updates the search revision once");
        Assert.That(current.PinyinEnabled, Is.False);
    }

    [Test]
    public void PendingClearsConfirmedLocaleForConfigApply()
    {
        var resolvedLocales = new List<string?>();
        var coordinator = new PinyinLifecycleCoordinator(locale =>
        {
            resolvedLocales.Add(locale);
            return new SearchConfiguration(enabled: false);
        }, _ => { }, initiallyEnabled: false);

        coordinator.OnLocaleChanged("zh", settled: true);
        coordinator.OnLocaleChanged(null, settled: false);
        coordinator.OnConfigApplied();

        Assert.That(resolvedLocales, Is.EqualTo(new string?[] { "zh", null, null }));
    }
}
