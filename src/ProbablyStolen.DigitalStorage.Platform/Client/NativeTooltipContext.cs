using System;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class NativeTooltipContext
{
    private readonly NativeNetworkScanner _scanner;
    private readonly Func<bool>? _isActive;
    private NativeGridSnapshot? _snapshot;
    private bool _captured;

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
    internal bool IsProjected { get; private set; }
    internal bool IsActive => _isActive?.Invoke() ?? true;
    internal NativeGridSnapshot Snapshot
    {
        get
        {
            if (!_captured)
            {
                var projected = ControllerTooltipBridge.FindSnapshot(Item);
                IsProjected = projected != null;
                _snapshot = projected ?? _scanner.Capture(Item);
                _captured = true;
            }
            return _snapshot!;
        }
    }

}
