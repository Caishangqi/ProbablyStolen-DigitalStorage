using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class NativeTooltipDispatcher
{
    private readonly NativeNetworkScanner _scanner;
    private readonly IReadOnlyList<INativeTooltipContributor> _contributors;
    private readonly Action<Identifier, Exception> _reportError;
    private bool _enabled = true;

    internal NativeTooltipDispatcher(NativeNetworkScanner scanner, IReadOnlyList<INativeTooltipContributor> contributors,
        Action<Identifier, Exception> reportError)
    {
        _scanner = scanner;
        _contributors = contributors;
        _reportError = reportError;
    }

    internal bool IsEnabled => _enabled;

    internal void Disable() => _enabled = false;

    internal void OnCreateTooltipLate(RichTextBuilder builder, GameItem item)
    {
        if (!_enabled || builder == null || item == null) return;
        var context = new NativeTooltipContext(builder, item, _scanner, () => _enabled);
        foreach (var contributor in _contributors)
        {
            if (!_enabled) return;
            try { contributor.Append(context); }
            catch (Exception error)
            {
                try { _reportError(contributor.Identity, error); }
                catch (Exception) { }
            }
        }
    }
}
