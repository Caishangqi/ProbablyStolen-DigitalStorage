namespace ProbablyStolen.DigitalStorage.Common.Contracts;

/// <summary>Immutable facts read from a physical item. It does not own or recreate that item.</summary>
public sealed class PhysicalItemSnapshot
{
    /// <summary>Creates the facts needed for slot admission and component removal.</summary>
    public PhysicalItemSnapshot(string? identifier, int count, int width, int height, bool fixedInPlace = false)
    {
        Identifier = identifier;
        Count = count;
        Width = width;
        Height = height;
        FixedInPlace = fixedInPlace;
    }

    /// <summary>The stable item definition, never a localized name.</summary>
    public string? Identifier { get; }

    /// <summary>The actual number of units on the original item.</summary>
    public int Count { get; }

    /// <summary>The unrotated physical width.</summary>
    public int Width { get; }

    /// <summary>The unrotated physical height.</summary>
    public int Height { get; }

    /// <summary>Whether native extraction must unlock the component first.</summary>
    public bool FixedInPlace { get; }
}
