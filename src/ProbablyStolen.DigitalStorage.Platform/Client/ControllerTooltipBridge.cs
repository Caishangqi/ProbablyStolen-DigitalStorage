using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Network;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Provides a temporary native power view during controller tooltip construction.</summary>
public static class ControllerTooltipBridge
{
    private static Registration? _registration;
    private static readonly List<Scope> Scopes = new();

    internal static IDisposable Register(NativeNetworkScanner scanner, Action<string> reportError)
    {
        var registration = new Registration(scanner, reportError);
        _registration = registration;
        return registration;
    }

    /// <summary>Begins a projection if the enabled reader can obtain a representable demand.</summary>
    public static IDisposable? Begin(GameItem item)
    {
        var registration = _registration;
        if (registration == null || item == null || item.identifier != ControllerItem.ItemId) return null;
        try
        {
            var snapshot = registration.Scanner.Capture(item);
            var demand = snapshot.FindGrid(item).TotalPowerUsage;
            if (!ReferenceEquals(_registration, registration) || demand <= 0 || demand > int.MaxValue) return null;
            var original = item.modifiedState;
            var projected = original.Clone();
            foreach (var pair in original.dict)
                projected.dict[pair.Key] = pair.Value.Clone();
            projected.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER).Enable().SetInt((int)demand);
            projected.GetTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).Enable().SetInt((int)demand);
            var scope = new Scope(item, original, projected, snapshot);
            Scopes.Add(scope);
            item.modifiedState = projected;
            return scope;
        }
        catch (Exception error)
        {
            try { registration.ReportError($"Digital Storage: controller tooltip projection failed ({error.GetType().Name}: {error.Message})."); }
            catch (Exception) { }
            return null;
        }
    }

    internal static NativeGridSnapshot? FindSnapshot(GameItem item)
    {
        for (var index = Scopes.Count - 1; index >= 0; index--)
            if (ReferenceEquals(Scopes[index].Item, item) && ReferenceEquals(item.modifiedState, Scopes[index].Projected))
                return Scopes[index].Snapshot;
        return null;
    }

    private sealed class Registration : IDisposable
    {
        internal readonly NativeNetworkScanner Scanner;
        internal readonly Action<string> ReportError;

        internal Registration(NativeNetworkScanner scanner, Action<string> reportError)
        {
            Scanner = scanner;
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
        private readonly TagSystem _original;
        private bool _disposed;

        internal Scope(GameItem item, TagSystem original, TagSystem projected, NativeGridSnapshot snapshot)
        {
            Item = item;
            _original = original;
            Projected = projected;
            Snapshot = snapshot;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(Item.modifiedState, Projected)) Item.modifiedState = _original;
            Scopes.Remove(this);
        }
    }
}
