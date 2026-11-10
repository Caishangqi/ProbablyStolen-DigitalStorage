using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class PinyinLifecycleCoordinator
{
    private readonly Func<string?, SearchConfiguration> _resolve;
    private readonly Action<SearchConfiguration> _apply;
    private string? _confirmedLocale;
    private bool _localePending;
    private bool _effectiveEnabled;

    internal PinyinLifecycleCoordinator(Func<string?, SearchConfiguration> resolve,
        Action<SearchConfiguration> apply, bool initiallyEnabled)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _effectiveEnabled = initiallyEnabled;
    }

    internal SearchConfiguration OnLocaleChanged(string? locale, bool settled)
    {
        _localePending = !settled;
        _confirmedLocale = settled ? locale : null;
        var configuration = _resolve(settled ? locale : null);
        if (settled || configuration.PinyinEnabled != _effectiveEnabled)
            Apply(configuration);
        return configuration;
    }

    internal SearchConfiguration OnConfigApplied()
    {
        var configuration = _resolve(_localePending ? null : _confirmedLocale);
        Apply(configuration);
        return configuration;
    }

    private void Apply(SearchConfiguration configuration)
    {
        _apply(configuration);
        _effectiveEnabled = configuration.PinyinEnabled;
    }
}
