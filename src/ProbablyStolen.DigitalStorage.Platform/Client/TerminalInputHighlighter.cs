using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class TerminalInputHighlighter : IDisposable
{
    private readonly TMP_InputField _input;
    private readonly TerminalSearchService _search;
    private readonly Func<string, bool> _hasResults;
    private bool _disposed;

    internal TerminalInputHighlighter(TMP_InputField input, TerminalSearchService search, Func<string, bool> hasResults)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _hasResults = hasResults ?? throw new ArgumentNullException(nameof(hasResults));
        _input.richText = false;
        _input.textComponent.OnPreRenderText += OnPreRenderText;
        _input.ForceLabelUpdate();
    }

    internal void Refresh() => _input.ForceLabelUpdate();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_input != null && _input.textComponent != null) _input.textComponent.OnPreRenderText -= OnPreRenderText;
    }

    private void OnPreRenderText(TMP_TextInfo info)
    {
        if (_disposed || info == null) return;
        var raw = _input.text ?? string.Empty;
        var inputSystem = EventSystem.current?.currentInputModule?.input;
        var composition = inputSystem != null ? inputSystem.compositionString : Input.compositionString;
        var compositionLength = _input.readOnly ? 0 : composition?.Length ?? 0;
        var insertionIndex = Math.Max(0, Math.Min(raw.Length, _input.selectionStringAnchorPosition - compositionLength));
        var matched = string.IsNullOrWhiteSpace(raw) || _hasResults(raw);
        var spans = _search.Configuration.SyntaxHighlighting
            ? _search.GetColorSpans(raw, matched)
            : Array.Empty<SearchColorSpan>();
        var colors = new uint[raw.Length];
        foreach (var span in spans)
        {
            var argb = span.Kind switch
            {
                SearchColorKind.Prefix => TerminalSearchService.PrefixArgb,
                SearchColorKind.PrefixedText => TerminalSearchService.PrefixedArgb,
                SearchColorKind.Operator => TerminalSearchService.OperatorArgb,
                SearchColorKind.NoMatch => TerminalSearchService.NoMatchArgb,
                _ => TerminalSearchService.OrdinaryArgb
            };
            for (var index = span.Start; index < span.Start + span.Length && index < colors.Length; index++)
                if (index >= 0) colors[index] = argb;
        }
        for (var i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible) continue;
            var sourceIndex = TerminalSearchService.CommittedIndex(character.index, insertionIndex, compositionLength);
            var argb = sourceIndex >= 0 && sourceIndex < colors.Length && colors[sourceIndex] != 0
                ? colors[sourceIndex] : TerminalSearchService.OrdinaryArgb;
            var color = new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
            var mesh = info.meshInfo[character.materialReferenceIndex];
            for (var vertex = 0; vertex < 4; vertex++) mesh.colors32[character.vertexIndex + vertex] = color;
        }
        for (var i = 0; i < info.meshInfo.Length; i++)
            info.meshInfo[i].mesh.colors32 = info.meshInfo[i].colors32;
    }
}
