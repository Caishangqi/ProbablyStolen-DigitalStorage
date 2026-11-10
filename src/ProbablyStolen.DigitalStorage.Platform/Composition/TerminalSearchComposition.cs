using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class TerminalSearchComposition : IDisposable
{
    private readonly PinyinSearchAddon _pinyin;
    private readonly string _modId;
    private readonly Action<string> _log;
    private bool _dictionaryAttempted;
    private readonly TerminalSearchService _service;
    private bool _disposed;

    internal TerminalSearchComposition(string modId, ModText text, SearchConfiguration configuration, Action<string> log,
        Action<SearchMatcherRegistry>? registerAddons = null)
    {
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("A mod ID is required.", nameof(modId));
        if (text == null) throw new ArgumentNullException(nameof(text));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modId = modId;
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, configuration => configuration.Tags));
        registry.RegisterField(new SearchFieldProvider("types", document => document.ItemTypes, configuration => configuration.ItemTypes));
        registry.RegisterField(new SearchFieldProvider("descriptions", document => document.Descriptions, configuration => configuration.Descriptions));
        registry.RegisterField(new SearchFieldProvider("identifier", document => new[] { document.Identifier }, configuration => configuration.Identifiers));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterPrefix('@', "types");
        registry.RegisterPrefix('$', "descriptions");
        registry.RegisterPrefix('&', "identifier");
        // The loader adds this mod's folder only after Init and OnEnable return. Register the stable
        // delegate now, but defer all file reads until OnModAssetsLoaded (Steam build 26525552).
        _pinyin = new PinyinSearchAddon(configuration);
        registry.RegisterMatcher("pinyin", _pinyin.Matches);

        try
        {
            registerAddons?.Invoke(registry);
            _service = new TerminalSearchService(registry, configuration);
        }
        catch
        {
            _pinyin?.Dispose();
            throw;
        }
    }

    internal TerminalSearchService Service => _service;

    internal void LoadDictionary()
    {
        if (_disposed || _dictionaryAttempted) return;
        _dictionaryAttempted = true;
        try
        {
            var dictionary = ModHelper.ReadModFileText(_modId, "Search/pinyin-dictionary.txt");
            if (string.IsNullOrWhiteSpace(dictionary))
            {
                _log("Digital Storage: pinyin search disabled (dictionary missing or empty after mod assets loaded).");
                return;
            }
            _pinyin.LoadDictionary(dictionary);
            _service.UpdateConfiguration(_service.Configuration);
        }
        catch (Exception error)
        {
            _log($"Digital Storage: pinyin search disabled after mod assets loaded ({error.GetType().Name}).");
        }
    }

    internal void Update(SearchConfiguration configuration)
    {
        if (_disposed) return;
        _pinyin.Configure(configuration);
        _service.UpdateConfiguration(configuration);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pinyin?.Dispose();
    }
}
