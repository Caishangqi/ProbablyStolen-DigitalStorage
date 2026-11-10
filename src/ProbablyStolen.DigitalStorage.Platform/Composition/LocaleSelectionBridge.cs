using System;
using System.Collections;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

/// <summary>Connects game locale selection coroutines to the active composition.</summary>
public static class LocaleSelectionBridge
{
    private static LocaleSelectionRouter? _owner;

    /// <summary>Registers the composition's locale callback and returns its lifetime token.</summary>
    public static IDisposable Register(Action<string?> notify, Action<string> log) =>
        RegisterCore((locale, _) => notify(locale), log);

    /// <summary>Registers a callback that also receives the settlement state.</summary>
    public static IDisposable RegisterSettled(Action<string?, bool> notify, Action<string> log) =>
        RegisterCore(notify, log);

    private static IDisposable RegisterCore(Action<string?, bool> notify, Action<string> log)
    {
        _owner?.Dispose();
        var owner = new LocaleSelectionRouter(ReadLocale, notify, log);
        _owner = owner;
        return new Registration(owner);
    }

    /// <summary>Wraps a game coroutine using the owner active at wrapping time.</summary>
    public static IEnumerator Wrap(IEnumerator coroutine) => _owner?.Wrap(coroutine) ?? coroutine;

    private static string? ReadLocale()
    {
        var handle = LocalizationSettings.SelectedLocaleAsync;
        return handle.IsValid() && handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded
            && handle.Result != null ? handle.Result.Identifier.Code : null;
    }

    private sealed class Registration : IDisposable
    {
        private readonly LocaleSelectionRouter _router;
        internal Registration(LocaleSelectionRouter router) => _router = router;
        public void Dispose()
        {
            _router.Dispose();
            if (ReferenceEquals(_owner, _router)) _owner = null;
        }
    }
}
