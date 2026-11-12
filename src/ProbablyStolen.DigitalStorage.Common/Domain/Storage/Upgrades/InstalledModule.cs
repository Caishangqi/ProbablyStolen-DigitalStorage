namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

/// <summary>One module in a cell's module bay, as read from its item.</summary>
public sealed class InstalledModule
{
    private InstalledModule(CellModuleKind kind, TypeLimitRoll? roll)
    {
        Kind = kind;
        Roll = roll;
    }

    /// <summary>Gets the module kind.</summary>
    public CellModuleKind Kind { get; }

    /// <summary>Gets the rolled values of a type limit module; null when they are missing or unreadable.</summary>
    public TypeLimitRoll? Roll { get; }

    /// <summary>Gets whether the module takes effect. An invalid type limit module adds nothing.</summary>
    public bool IsValid => Kind switch
    {
        CellModuleKind.EqualDistribution => true,
        CellModuleKind.TypeLimit => Roll != null && TypeLimitRolls.IsValid(Roll),
        _ => false
    };

    /// <summary>Creates an equal distribution module.</summary>
    public static InstalledModule EqualDistribution() => new(CellModuleKind.EqualDistribution, null);

    /// <summary>Creates a type limit module; a null roll stands for values that could not be read.</summary>
    public static InstalledModule TypeLimit(TypeLimitRoll? roll) => new(CellModuleKind.TypeLimit, roll);

    /// <inheritdoc />
    public override string ToString() => Roll == null ? Kind.ToString() : $"{Kind}({Roll})";
}
