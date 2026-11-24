namespace ProbablyStolen.DigitalStorage.Common.Domain.Machines;

/// <summary>When an input cell is done and moves to the output bank.</summary>
public enum IoPortCompletionMode
{
    /// <summary>Done when the cell holds no stored roots.</summary>
    Empty = 0,

    /// <summary>Done when the cell cannot accept more storage.</summary>
    Full = 1,

    /// <summary>Done when a batch moves nothing only because the source ran out or the target refused every item.</summary>
    WorkComplete = 2
}
