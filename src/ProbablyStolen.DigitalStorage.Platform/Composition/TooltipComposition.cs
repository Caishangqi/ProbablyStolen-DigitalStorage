using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Network;
using ProbablyStolen.DigitalStorage.Platform.Storage;
#if !DIGITAL_STORAGE_INTEGRATION_TESTS
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using UnityEngine;
#endif

namespace ProbablyStolen.DigitalStorage.Platform.Composition;

internal sealed class TooltipComposition : IDisposable
{
    private readonly RegistryManager _registries = new();
    private readonly NativeNetworkScanner _scanner;
    private readonly Action<string> _reportError;
    private readonly HashSet<Identifier> _reportedFailures = new();
    private NativeTooltipDispatcher? _dispatcher;
#if !DIGITAL_STORAGE_INTEGRATION_TESTS
    private NativeTooltipService? _service;
#endif
    private GridNetworkTooltipPresenter? _networkPresenter;
    private IDisposable? _projection;
    private bool _subscribed;
    private bool _disposed;

    internal TooltipComposition(NativeNetworkScanner scanner, GridNetworkPowerCoordinator power,
        ModText text, BasicCellHandler cells, Func<GameItem, CellModuleUsage?> findWorkbenchUsage, Action<string> reportError)
    {
        _scanner = scanner;
        _reportError = reportError;
        var contributorsId = ContributorsId;
        var detailsId = DetailsId;
        _registries.NewRegistry += e =>
        {
            e.Create<INativeTooltipContributor>(contributorsId);
            e.Create<INetworkTooltipDetail>(detailsId);
        };
        var details = DeferredRegister<INetworkTooltipDetail>.Create(_registries, detailsId);
        details.Register("controller", () => new ControllerTooltipDetail(text));
        details.Register("drive", () => new DriveTooltipDetail(text));
        var contributors = DeferredRegister<INativeTooltipContributor>.Create(_registries, contributorsId);
        contributors.Register("cell", () => new CellTooltipContributor(cells));
        contributors.Register("module", () => new ModuleTooltipContributor(text, findWorkbenchUsage));
        contributors.Register("network", () => _networkPresenter = new GridNetworkTooltipPresenter(
            power, text, Array.Empty<INetworkTooltipDetail>(), ReportFailure));
    }

#if !DIGITAL_STORAGE_INTEGRATION_TESTS
    internal ITooltipService Service => _service ?? throw new InvalidOperationException("Tooltip composition is not enabled.");

    internal void Enable(Func<Sprite?> rgbFrame)
#else
    internal void Enable()
#endif
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TooltipComposition));
        if (_dispatcher != null) return;
        try
        {
            _registries.CreateRegistries();
            _registries.RegisterEntries();
            _registries.Freeze();
            _networkPresenter!.SetDetails(_registries.Get<INetworkTooltipDetail>(DetailsId)
                .RegistrationEntries.Select(entry => entry.Value).ToArray());
            _dispatcher = new NativeTooltipDispatcher(_scanner,
                _registries.Get<INativeTooltipContributor>(ContributorsId)
                    .RegistrationEntries.Select(entry => entry.Value).ToArray(), ReportFailure);
#if !DIGITAL_STORAGE_INTEGRATION_TESTS
            _service = new NativeTooltipService(rgbFrame, _reportError);
            TerminalTooltipPresenter.SetService(_service);
#endif
            _projection = NetworkPowerTooltipBridge.Register(_scanner, _reportError);
            _subscribed = true;
            ModHook.OnCreateTooltipLate += _dispatcher.OnCreateTooltipLate;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var dispatcher = _dispatcher;
        if (dispatcher != null) dispatcher.Disable();
        if (_subscribed && dispatcher != null)
        {
            try { ModHook.OnCreateTooltipLate -= dispatcher.OnCreateTooltipLate; }
            catch (Exception) { }
            _subscribed = false;
        }
        _dispatcher = null;
#if !DIGITAL_STORAGE_INTEGRATION_TESTS
        _service?.Dispose();
        _service = null;
        TerminalTooltipPresenter.SetService(null);
#endif
        _projection?.Dispose();
        _projection = null;
        _registries.Dispose();
    }

    private void ReportFailure(Identifier identity, Exception error)
    {
        if (!_reportedFailures.Add(identity)) return;
        try
        {
            _reportError($"Digital Storage: native tooltip handler {identity} failed ({error.GetType().Name}).");
        }
        catch (Exception) { }
    }

    private static readonly Identifier ContributorsId = new("digital_storage", "native_tooltip_contributors");
    private static readonly Identifier DetailsId = new("digital_storage", "network_tooltip_details");
}
