namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>Parts of an item's identity that no stacking rule can ignore, named when two selected entries differ in them.</summary>
public static class ItemIdentityPart
{
    /// <summary>The item's stored state could not be read, so its entry has an identity of its own.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>The item types the game assigns.</summary>
    public const string ItemTypes = "item_types";

    /// <summary>The item name.</summary>
    public const string Name = "name";

    /// <summary>The short description.</summary>
    public const string ShortDescription = "short_description";

    /// <summary>The flavor text.</summary>
    public const string FlavorText = "flavor_text";

    /// <summary>The sprite atlas or sprite.</summary>
    public const string Sprite = "sprite";

    /// <summary>The items in the examine window.</summary>
    public const string ExamineWindow = "examine_window";
}
