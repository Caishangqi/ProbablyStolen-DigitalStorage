using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Common.Application.Events;

/// <summary>
/// Dispatches typed storage events synchronously to their subscribers. Every call is made on the game's main
/// thread; the bus performs no locking.
/// </summary>
/// <remarks>
/// Native item callbacks and terminal open callbacks keep a reference to the bus after the mod is disabled, so
/// posting to or subscribing on a disposed bus is a no-op rather than an error.
/// </remarks>
public sealed class StorageEventBus : IDisposable
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly Action<string> _reportError;
    private bool _disposed;

    /// <summary>Creates a bus that reports subscriber failures without interrupting other subscribers.</summary>
    /// <exception cref="ArgumentNullException">The error reporter is null.</exception>
    public StorageEventBus(Action<string> reportError)
    {
        _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
    }

    /// <summary>Adds a handler for one event type; disposing the result removes it. A disposed bus adds nothing.</summary>
    /// <exception cref="ArgumentNullException">The handler is null.</exception>
    public IDisposable Subscribe<T>(Action<T> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (_disposed) return new Subscription(null);

        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            list = new List<Delegate>();
            _handlers.Add(typeof(T), list);
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    /// <summary>Invokes the current handlers of the event type; handlers added or removed meanwhile apply next time.</summary>
    public void Post<T>(T storageEvent)
    {
        if (_disposed || !_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;

        foreach (var handler in list.ToArray())
        {
            try
            {
                ((Action<T>)handler)(storageEvent);
            }
            catch (Exception error)
            {
                _reportError($"Digital Storage: {typeof(T).Name} handler failed ({error.GetType().Name}: {error.Message}).");
            }
        }
    }

    /// <summary>Drops every subscription; later posts are ignored.</summary>
    public void Dispose()
    {
        _disposed = true;
        _handlers.Clear();
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _remove;

        internal Subscription(Action? remove) => _remove = remove;

        public void Dispose()
        {
            _remove?.Invoke();
            _remove = null;
        }
    }
}
