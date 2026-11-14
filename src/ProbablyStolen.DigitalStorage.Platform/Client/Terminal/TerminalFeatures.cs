using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Diagnostics;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.StorageTerminal;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.TerminalStyle;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal.Features.Modules;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

/// <summary>
/// Registers the built-in terminal features once, at mod start. Each feature joins through its own <c>Register</c>,
/// the same path module features take; the platform supplies only the view and page factories, which receive the
/// <see cref="TerminalWindow"/> as the context's window handle.
/// </summary>
internal static class TerminalFeatures
{
    internal static TerminalRegistry Create(IConfigManager config, Action<string> log)
    {
        var registry = new TerminalRegistry(config, log);
        StorageTerminalFeature.Register(registry, window => ((TerminalWindow)window).CreateStorageView());
        ModulesFeature.Register(registry, window => new TerminalModulesScreen((TerminalWindow)window));
        TerminalStyleFeature.Register(registry);
        DiagnosticsFeature.Register(registry, window => new DiagnosticTerminalView((TerminalWindow)window),
            window => new DiagnosticScreen((TerminalWindow)window), log);
        registry.Freeze();
        return registry;
    }
}
