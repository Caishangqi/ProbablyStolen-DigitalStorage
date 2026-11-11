namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Extra item types one source grants a cell.</summary>
public readonly struct TypeLimitExtension
{
    /// <summary>Creates an extension.</summary>
    public TypeLimitExtension(string sourceId, int bonus)
    {
        SourceId = sourceId;
        Bonus = bonus;
    }

    /// <summary>Gets the stable identity of the granting source.</summary>
    public string SourceId { get; }

    /// <summary>Gets the extra type count.</summary>
    public int Bonus { get; }
}
