using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Storage terminal adapter that supplies storage services to the shared terminal window host.</summary>
internal sealed class TerminalWindow : TerminalWindowHost
{
    private readonly StorageTerminalServices _storage;

    internal TerminalWindow(TerminalRegistry registry, StorageTerminalServices storage, ITerminalViewSettings settings,
        Func<bool> testViewEnabled, ButtonIcons icons, SlotMetricsService slotMetrics)
        : base(registry, storage, settings, testViewEnabled, icons, slotMetrics)
    {
        _storage = storage;
    }

    internal override ITerminalView CreateStorageView() => new StorageTerminalView(_storage, this);

    /// <summary>The storage services of this terminal, used by pages that read the stored items.</summary>
    internal StorageTerminalServices Storage => _storage;
}
