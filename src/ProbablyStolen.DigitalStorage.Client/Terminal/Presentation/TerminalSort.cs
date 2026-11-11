namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Selects the terminal ordering key.</summary>
public enum TerminalSortOrder
{
    /// <summary>Orders entries by display name.</summary>
    Name,

    /// <summary>Orders entries by stored amount.</summary>
    Amount
}

/// <summary>Selects the terminal ordering direction.</summary>
public enum TerminalSortDirection
{
    /// <summary>Smallest name or amount first.</summary>
    Ascending,

    /// <summary>Largest name or amount first.</summary>
    Descending
}

/// <summary>Terminal sizes; the value is the quarter of available rows shown.</summary>
public enum TerminalSize
{
    /// <summary>A quarter of the rows that fit.</summary>
    Small = 1,

    /// <summary>Half of the rows that fit.</summary>
    Medium = 2,

    /// <summary>Three quarters of the rows that fit.</summary>
    Tall = 3,

    /// <summary>All rows that fit.</summary>
    Full = 4
}
