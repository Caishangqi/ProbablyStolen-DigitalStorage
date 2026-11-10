using System;
using System.Collections;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

// One instance belongs to one composition. Wrappers retain that instance, never a later owner.
internal sealed class LocaleSelectionRouter : IDisposable
{
    private readonly Func<string?> _readLocale;
    private readonly Action<string?, bool> _notify;
    private readonly Action<string> _log;
    private int _active;
    private bool _confirmed;
    private bool _disposed;
    private bool _logged;

    internal LocaleSelectionRouter(Func<string?> readLocale, Action<string?> notify, Action<string> log)
        : this(readLocale, (locale, _) => notify(locale), log)
    {
    }

    internal LocaleSelectionRouter(Func<string?> readLocale, Action<string?, bool> notify, Action<string> log)
    {
        _readLocale = readLocale;
        _notify = notify;
        _log = log;
    }

    internal IEnumerator Wrap(IEnumerator inner) => new Wrapped(this, inner);

    public void Dispose() => _disposed = true;

    private void Start()
    {
        if (_disposed) return;
        _active++;
        Notify(null, false);
    }

    private void Finish(bool completed)
    {
        if (_disposed) return;
        _active--;
        _confirmed |= completed;
        if (_active != 0 || !_confirmed) return;
        string? locale = null;
        try
        {
            locale = _readLocale();
            if (locale == null) Log("Current game locale is unavailable.");
        }
        catch (Exception)
        {
            Log("Could not read the current game locale.");
        }
        Notify(locale, true);
    }

    private void Notify(string? locale, bool settled)
    {
        try { _notify(locale, settled); }
        catch (Exception) { Log("Locale notification failed."); }
    }

    private void Log(string message)
    {
        if (_logged) return;
        _logged = true;
        try { _log(message); }
        catch (Exception) { }
    }

    private sealed class Wrapped : IEnumerator, IDisposable
    {
        private readonly LocaleSelectionRouter _owner;
        private readonly IEnumerator _inner;
        private bool _started;
        private bool _finished;

        internal Wrapped(LocaleSelectionRouter owner, IEnumerator inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public object Current
        {
            get
            {
                try { return _inner.Current; }
                catch { End(false, true); throw; }
            }
        }

        public bool MoveNext()
        {
            if (_finished) return false;
            if (!_started)
            {
                _started = true;
                _owner.Start();
            }
            try
            {
                if (_inner.MoveNext()) return true;
            }
            catch { End(false, true); throw; }
            End(true, false);
            return false;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => End(false, false);

        private void End(bool completed, bool suppressDisposeError)
        {
            if (_finished) return;
            _finished = true;
            try
            {
                (_inner as IDisposable)?.Dispose();
            }
            catch
            {
                completed = false;
                if (!suppressDisposeError) throw;
            }
            finally
            {
                if (_started) _owner.Finish(completed);
            }
        }
    }
}
