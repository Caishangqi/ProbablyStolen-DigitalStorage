using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class TerminalSearchComposition : IDisposable
{
    private readonly PinyinSearchAddon _pinyin;
    private readonly string _modId;
    private readonly Action<string> _log;
    private readonly TerminalSearchService _service;
    private readonly DictionaryLoadGate _dictionaryGate;
    private SearchConfiguration _configuration;
    private bool _disposed;

    internal TerminalSearchComposition(string modId, ModText text, SearchConfiguration configuration, Action<string> log,
        Action<SearchMatcherRegistry>? registerAddons = null)
    {
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("A mod ID is required.", nameof(modId));
        if (text == null) throw new ArgumentNullException(nameof(text));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _modId = modId;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        var registry = new SearchMatcherRegistry();
        registry.RegisterField(new SearchFieldProvider("tags", document => document.TagNames, config => config.Tags));
        registry.RegisterField(new SearchFieldProvider("types", document => document.ItemTypes, config => config.ItemTypes));
        registry.RegisterField(new SearchFieldProvider("descriptions", document => document.Descriptions, config => config.Descriptions));
        registry.RegisterField(new SearchFieldProvider("identifier", document => new[] { document.Identifier }, config => config.Identifiers));
        registry.RegisterPrefix('#', "tags");
        registry.RegisterPrefix('@', "types");
        registry.RegisterPrefix('$', "descriptions");
        registry.RegisterPrefix('&', "identifier");
        _pinyin = new PinyinSearchAddon(configuration);
        registry.RegisterMatcher("pinyin", _pinyin.Matches);
        _dictionaryGate = new DictionaryLoadGate(ReadDictionary);
        _dictionaryGate.Update(configuration.PinyinEnabled);

        try
        {
            registerAddons?.Invoke(registry);
            _service = new TerminalSearchService(registry, configuration);
        }
        catch
        {
            _dictionaryGate.Dispose();
            _pinyin.Dispose();
            throw;
        }
    }

    internal TerminalSearchService Service => _service;

    internal void MarkAssetsReady()
    {
        if (_disposed) return;
        if (_dictionaryGate.MarkAssetsReady() && !_disposed) _service.UpdateConfiguration(_configuration);
    }

    internal void Update(SearchConfiguration configuration)
    {
        if (_disposed) return;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _pinyin.Configure(configuration);
        _dictionaryGate.Update(configuration.PinyinEnabled);
        if (!_disposed) _service.UpdateConfiguration(configuration);
    }

    private void ReadDictionary()
    {
        try
        {
            var dictionary = ModHelper.ReadModFileText(_modId, "Search/pinyin-dictionary.txt");
            if (_disposed) return;
            if (string.IsNullOrWhiteSpace(dictionary))
            {
                _log("Digital Storage: pinyin search disabled (dictionary missing or empty after mod assets loaded).");
                return;
            }
            _pinyin.LoadDictionary(dictionary);
        }
        catch (Exception error)
        {
            _log($"Digital Storage: pinyin search disabled after mod assets loaded ({error.GetType().Name}).");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dictionaryGate.Dispose();
        _pinyin.Dispose();
    }
}
