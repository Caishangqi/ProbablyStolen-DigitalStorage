using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// What the stacking feature needs from the platform window it runs in. Each method receives
/// <see cref="TerminalContext.Window"/>.
/// </summary>
public interface IStackingPlatform
{
    /// <summary>Creates the stacking rules page.</summary>
    ITerminalScreen CreateRulesScreen(object window);

    /// <summary>Creates the editor page of one item's rule.</summary>
    ITerminalScreen CreateItemEditor(object window, string itemId);

    /// <summary>Reads the values projectors may compare, such as the estimated value, for the given entries.</summary>
    EntryAttributes ReadAttributes(object window, IReadOnlyList<TerminalEntry> entries);

    /// <summary>Tells the window that saved rules changed, so the storage view groups again.</summary>
    void Regroup(object window);
}
