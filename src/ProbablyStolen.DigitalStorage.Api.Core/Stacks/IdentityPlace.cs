namespace ProbablyStolen.DigitalStorage.Api.Stacks;

/// <summary>
/// Formats where in an identity a rule was met, such as <c>coffee_bean/tag:KEY</c>. Keys and identifiers come from other
/// mods and may be up to 64 KiB long, so each name is cut to a short prefix with its length.
/// </summary>
internal static class IdentityPlace
{
    private const int MaxNameLength = 64;

    internal static string Format(string item, string? partPrefix, string? partName)
    {
        var place = Cut(item);
        return partPrefix == null ? place : place + "/" + partPrefix + Cut(partName ?? "");
    }

    private static string Cut(string name) =>
        name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength) + "...(len=" + name.Length + ")";
}
