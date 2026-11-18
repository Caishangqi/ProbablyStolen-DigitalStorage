using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Projects network power into native machine tooltips for a single synchronous tooltip call.</summary>
public static class NetworkPowerTooltipBridge
{
    private static Registration? _registration;
    private static readonly List<Scope> Scopes = new();

    internal static IDisposable Register(NativeNetworkScanner scanner, Action<string> reportError)
    {
        var registration = new Registration(scanner, reportError);
        _registration = registration;
        return registration;
    }

    /// <summary>Creates a temporary view of the machine's modified state for native tooltip readers.</summary>
    public static IDisposable? Begin(GameItem item)
    {
        var registration = _registration;
        if (registration == null || item == null || !NativeNetworkScanner.IsNetworkMachine(item)) return null;
        try
        {
            var snapshot = registration.Scanner.Capture(item);
            var original = item.modifiedState;
            var projected = original.Clone();
            foreach (var pair in original.dict)
                projected.dict[pair.Key] = pair.Value.Clone();

            if (item.identifier == ControllerItem.ItemId)
            {
                var demand = snapshot.FindGrid(item).TotalPowerUsage;
                if (demand < 0 || demand > int.MaxValue) return null;
                projected.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt((int)demand);
                projected.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).Enable().SetInt((int)demand);
            }
            else
            {
                // Terminal and drive tooltips must not run the native local-battery check.
                projected.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).SetInt(0);
            }

            var scope = new Scope(item, original, projected, snapshot);
            Scopes.Add(scope);
            item.modifiedState = projected;
            return scope;
        }
        catch (Exception error)
        {
            try { registration.ReportError($"Digital Storage: network tooltip projection failed ({error.GetType().Name}: {error.Message})."); }
            catch (Exception) { }
            return null;
        }
    }

    internal static NativeGridSnapshot? FindSnapshot(GameItem item)
    {
        for (var index = Scopes.Count - 1; index >= 0; index--)
        {
            var scope = Scopes[index];
            if (ReferenceEquals(scope.Item, item) && ReferenceEquals(item.modifiedState, scope.Projected))
                return scope.Snapshot;
        }

        return null;
    }

    private sealed class Registration : IDisposable
    {
        internal readonly Action<string> ReportError;
        internal readonly NativeNetworkScanner Scanner;

        internal Registration(NativeNetworkScanner scanner, Action<string> reportError)
        {
            Scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            ReportError = reportError;
        }

        public void Dispose()
        {
            if (ReferenceEquals(_registration, this)) _registration = null;
        }
    }

    private sealed class Scope : IDisposable
    {
        internal readonly GameItem Item;
        internal readonly TagSystem Projected;
        internal readonly NativeGridSnapshot Snapshot;
        private readonly GameItem _item;
        private readonly TagSystem _original;
        private readonly TagSystem _projected;
        private bool _disposed;

        internal Scope(GameItem item, TagSystem original, TagSystem projected, NativeGridSnapshot snapshot)
        {
            Item = item;
            Projected = projected;
            Snapshot = snapshot;
            _item = item;
            _original = original;
            _projected = projected;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_item.modifiedState, _projected)) _item.modifiedState = _original;
            Scopes.Remove(this);
        }
    }
}
