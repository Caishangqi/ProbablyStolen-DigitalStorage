using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.StorageTerminal;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>Storage terminal adapter that supplies storage services to the shared terminal window host.</summary>
internal sealed class TerminalWindow : TerminalWindowHost
{
    private readonly StorageTerminalServices _storage;

    internal TerminalWindow(TerminalRegistry registry, StorageTerminalServices storage, ITerminalViewSettings settings,
        Func<bool> testViewEnabled) : base(registry, storage, settings, testViewEnabled)
    {
        _storage = storage;
    }

    internal override ITerminalView CreateStorageView() => new StorageTerminalView(_storage, this);
}
