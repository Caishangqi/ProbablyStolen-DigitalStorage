using System;

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

/// <summary>Reads stored terminal view choices; a value outside the enum reads as the default.</summary>
public static class TerminalViewPolicy
{
    /// <summary>Returns the order, or <see cref="TerminalSortOrder.Name"/> when it is undefined.</summary>
    public static TerminalSortOrder Normalize(TerminalSortOrder order) =>
        Enum.IsDefined(typeof(TerminalSortOrder), order) ? order : TerminalSortOrder.Name;

    /// <summary>Returns the direction, or <see cref="TerminalSortDirection.Ascending"/> when it is undefined.</summary>
    public static TerminalSortDirection Normalize(TerminalSortDirection direction) =>
        Enum.IsDefined(typeof(TerminalSortDirection), direction) ? direction : TerminalSortDirection.Ascending;

    /// <summary>Returns the size, or the fallback (<see cref="TerminalSize.Small"/> unless given) when it is undefined.</summary>
    public static TerminalSize Normalize(TerminalSize size, TerminalSize fallback = TerminalSize.Small) =>
        Enum.IsDefined(typeof(TerminalSize), size) ? size : fallback;
}
