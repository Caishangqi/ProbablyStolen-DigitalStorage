using System;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class NativeTooltipContext
{
    private readonly NativeNetworkScanner _scanner;
    private readonly Func<bool>? _isActive;
    private NativeGridSnapshot? _snapshot;

    internal NativeTooltipContext(RichTextBuilder builder, GameItem item, NativeNetworkScanner scanner,
        Func<bool>? isActive = null)
    {
        Builder = builder;
        Item = item;
        _scanner = scanner;
        _isActive = isActive;
    }

    internal RichTextBuilder Builder { get; }
    internal GameItem Item { get; }
    internal bool IsActive => _isActive?.Invoke() ?? true;
    internal NativeGridSnapshot Snapshot => _snapshot ??= NetworkPowerTooltipBridge.FindSnapshot(Item) ?? _scanner.Capture(Item);
}
