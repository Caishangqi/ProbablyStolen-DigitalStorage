namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>Which way an IO Port moves items between its input cells and the network.</summary>
public enum IoPortDirection
{
    /// <summary>Move the input cells' roots into the network.</summary>
    Forward = 0,

    /// <summary>Move network roots into the input cells.</summary>
    Reverse = 1
}
