namespace ProbablyStolen.DigitalStorage.Client.Tooltips;

/// <summary>Formats literal text for a TextMeshPro label that also displays trusted markup.</summary>
internal static class TooltipLiteralText
{
    // Isolate every opening bracket so even a literal closing noparse tag cannot change parsing state.
    internal static string ForRichText(string value) => value.Replace("<", "<noparse><</noparse>");
}
