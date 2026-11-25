using System.Collections.Generic;
using System.Text;
using ProbablyStolen.DigitalStorage.Client.Terminal.Search;
using ProbablyStolen.DigitalStorage.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Search box conveniences: a right click clears the box, and hovering shows what the box searches and its syntax.
/// </summary>
internal static class SearchInputs
{
    private static readonly string NoteColor =
        "#" + ColorUtility.ToHtmlStringRGB((Color)RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.Gray));

    /// <summary>Lets a right click clear the box; clearing raises the same change callbacks as typing.</summary>
    internal static void AttachClear(CustomUIElement? element)
    {
        var input = element?.input;
        if (input == null) return;
        // Unity's destroyed-object null does not survive "??", so the component is looked up explicitly.
        var clear = input.gameObject.GetComponent<RightClickClear>();
        if (clear == null) clear = input.gameObject.AddComponent<RightClickClear>();
        clear.Input = input;
    }

    /// <summary>Lets a right click clear the box and shows a hint led by <paramref name="titleKey"/>.</summary>
    /// <param name="element">The search box.</param>
    /// <param name="text">The localized text.</param>
    /// <param name="titleKey">What the box searches without any syntax.</param>
    /// <param name="search">The terminal search, which decides the active prefixes and pinyin.</param>
    /// <param name="syntax">Whether the box accepts prefixes and operators.</param>
    internal static void Attach(CustomUIElement? element, ModText text, string titleKey, TerminalSearchService search,
        bool syntax)
    {
        if (element?.input == null) return;
        AttachClear(element);
        TerminalTooltipPresenter.SetText(element, Hint(text, titleKey, TerminalSearchHint.Lines(search, syntax)));
    }

    // Laid out like a native item tooltip: a bold title, a short gap, the body, and the usage note in gray italics.
    private static string Hint(ModText text, string titleKey, IReadOnlyList<SearchHintLine> lines)
    {
        var hint = new StringBuilder("<b>").Append(Literal(text.Get(titleKey))).Append("</b>");
        hint.Append("\n<size=50%> </size>");
        foreach (var line in lines)
        {
            var value = Literal(line.Prefix is { } character ? text.Get(line.Key, character.ToString()) : text.Get(line.Key));
            hint.Append('\n');
            if (line.Key == TerminalSearchHint.ClearKey)
                hint.Append("<i><color=").Append(NoteColor).Append('>').Append(value).Append("</color></i>");
            else
                hint.Append(value);
        }

        return hint.ToString();
    }

    // The lines quote search syntax, which may hold "<", literally.
    private static string Literal(string value) => TooltipLiteralText.ForRichText(value);

    private sealed class RightClickClear : MonoBehaviour, IPointerClickHandler
    {
        internal TMP_InputField? Input { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right || Input == null || Input.text.Length == 0) return;
            Input.text = string.Empty;
        }
    }
}
