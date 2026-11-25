using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Extensions;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Stacking;

/// <summary>The storage terminal window's side of the stacking feature.</summary>
internal sealed class StackingPlatform : IStackingPlatform
{
    public ITerminalScreen CreateRulesScreen(object window)
    {
        var terminal = (TerminalWindow)window;
        return new StackingRulesScreen(terminal, terminal.Storage, this);
    }

    public ITerminalScreen CreateItemEditor(object window, string itemId)
    {
        var terminal = (TerminalWindow)window;
        return new StackingItemEditorScreen(terminal, terminal.Storage, itemId);
    }

    /// <summary>Reads fresh values, since the storage view reads none while no rule is active.</summary>
    public EntryAttributes ReadAttributes(object window, IReadOnlyList<TerminalEntry> entries)
    {
        var storage = ((TerminalWindow)window).Storage;
        return new EntryAttributeReader(storage.AttributeCaptures, storage.FindSample, storage.WriteDiagnostic)
            .Read(entries, StackingProjector.AttributeNames);
    }

    public void Regroup(object window) => ((TerminalWindowHost)window).StoragePipeline.Invalidate();
}
