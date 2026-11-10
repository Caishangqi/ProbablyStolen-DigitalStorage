using System;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class DictionaryLoadGate : IDisposable
{
    private Action? _load;
    private bool _assetsReady;
    private bool _enabled;
    private bool _attempted;
    private bool _disposed;

    internal DictionaryLoadGate(Action load) => _load = load ?? throw new ArgumentNullException(nameof(load));

    internal bool Attempted => _attempted;

    internal bool Update(bool enabled)
    {
        if (_disposed) return false;
        _enabled = enabled;
        return TryLoad();
    }

    internal bool MarkAssetsReady()
    {
        if (_disposed) return false;
        _assetsReady = true;
        return TryLoad();
    }

    private bool TryLoad()
    {
        if (_attempted || !_assetsReady || !_enabled || _disposed) return false;
        _attempted = true;
        var load = _load;
        if (load == null) return false;
        load();
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _load = null;
    }
}
