using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Tooltips;

/// <summary>Keeps natural measurement separate from the fitted rendering, including mutable-content resubmissions.</summary>
internal sealed class TooltipContentCache<T> where T : class
{
    internal const int DefaultCharacters = 60;
    internal const int UnwrappedCharacters = 1_000_000;
    private int _naturalCharacters = DefaultCharacters;
    private readonly Func<int, T> _build;
    private readonly IEqualityComparer<T> _comparer;
    private T? _natural;
    private T? _fitted;
    private int _fittedCharacters;
    private bool _submitted = true;
    private bool _fittedDirty = true;
    private bool _measured;
    private T? _applied;
    private int _appliedCharacters;
    private bool _appliedMetricsDirty = true;
    private object? _root;
    private object? _font;
    private object? _locale;
    private (double Scale, int Width, int Height, double Size) _metrics;

    internal TooltipContentCache(Func<int, T> build, IEqualityComparer<T> comparer)
    {
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
    }

    internal T Natural => _natural ?? throw new InvalidOperationException("Natural content has not been built.");
    internal double NaturalWidth { get; private set; }
    internal int NaturalCharacters => _naturalCharacters;

    internal void SetExpandToContent(bool expand)
    {
        var characters = expand ? UnwrappedCharacters : DefaultCharacters;
        if (_naturalCharacters == characters) return;
        _naturalCharacters = characters;
        InvalidateMetrics();
    }

    internal void Submit()
    {
        // A caller may have mutated the same builder. Reference identity is never a submission revision.
        _submitted = true;
        _fittedDirty = true;
    }

    internal void InvalidateMetrics()
    {
        Submit();
        _measured = false;
        _appliedMetricsDirty = true;
    }

    internal bool NeedsApply(T content, int characters) => _appliedMetricsDirty ||
        _appliedCharacters != characters || _applied == null || !_comparer.Equals(_applied, content);

    internal void RecordApplied(T content, int characters)
    {
        _applied = content;
        _appliedCharacters = characters;
        _appliedMetricsDirty = false;
    }

    internal bool UpdateMetrics(object root, object? font, object? locale,
        double scale, int width, int height, double size)
    {
        var metrics = (scale, width, height, size);
        if (ReferenceEquals(_root, root) && ReferenceEquals(_font, font) && ReferenceEquals(_locale, locale) && _metrics.Equals(metrics))
            return false;
        _root = root;
        _font = font;
        _locale = locale;
        _metrics = metrics;
        InvalidateMetrics();
        return true;
    }

    internal bool RefreshNatural()
    {
        if (_submitted)
        {
            var next = _build(NaturalCharacters);
            if (_natural == null || !_comparer.Equals(_natural, next)) _measured = false;
            _natural = next;
            _submitted = false;
        }
        return !_measured;
    }

    internal void RecordNaturalWidth(double width)
    {
        NaturalWidth = width;
        _measured = true;
    }

    internal T Fitted(int characters)
    {
        if (characters == NaturalCharacters) return Natural;
        if (_fitted == null || _fittedDirty || _fittedCharacters != characters)
        {
            _fitted = _build(characters);
            _fittedCharacters = characters;
            _fittedDirty = false;
        }
        return _fitted;
    }

    internal static int FitCharacters(double naturalWidth, double availableWidth) => naturalWidth > availableWidth
        ? Math.Max(8, (int)(DefaultCharacters * Math.Max(1, availableWidth) / naturalWidth))
        : DefaultCharacters;

    internal int FitContent(double availableWidth, int chrome) =>
        Math.Max(1, (int)Math.Min(Math.Ceiling(NaturalWidth), Math.Floor(availableWidth)) - chrome);
}
