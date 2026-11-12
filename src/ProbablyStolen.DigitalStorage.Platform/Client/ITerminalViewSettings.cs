using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// The terminal and partition editor view a player last chose, shared by every terminal, cell and save; setting a
/// value saves it.
/// </summary>
internal interface ITerminalViewSettings
{
    TerminalSortOrder SortOrder { get; set; }

    TerminalSortDirection SortDirection { get; set; }

    TerminalSize Size { get; set; }

    TerminalSize PartitionSize { get; set; }
}
