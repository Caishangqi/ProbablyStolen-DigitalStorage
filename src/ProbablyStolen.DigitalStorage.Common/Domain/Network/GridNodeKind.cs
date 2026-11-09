namespace ProbablyStolen.DigitalStorage.Common.Domain.Network;

/// <summary>The platform-independent kind of a machine node in a DigitalStorage grid.</summary>
public enum GridNodeKind
{
    /// <summary>The only controller in a connected network.</summary>
    Controller,

    /// <summary>A storage drive attached to the network.</summary>
    Drive,

    /// <summary>A terminal attached to the network.</summary>
    Terminal
}

