using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Platform.Client;

internal sealed class RichTextBuilder
{
    internal readonly List<string> Lines = new();
    internal Action<string>? OnAdd;
    internal string? ThrowOn;
    internal void Add(string value, object color)
    {
        if (value == ThrowOn) throw new InvalidOperationException("injected tooltip output failure");
        Lines.Add(value);
        OnAdd?.Invoke(value);
    }
    internal void AddLine(string value, bool ignoreEmpty, object color)
    {
        if (value == ThrowOn) throw new InvalidOperationException("injected tooltip output failure");
        Lines.Add(value);
        OnAdd?.Invoke(value);
    }
}

internal sealed class CustomUIManager
{
    internal const int LAYER_PAPER = 0;
    internal static CustomUIManager Instance => throw new InvalidOperationException("unexpected restore warning");
    internal static CustomUIManager Window(string id, string title, int layer) => Instance;
    internal CustomUIManager AddWarning(string message) => throw new InvalidOperationException("unexpected warning UI");
    internal CustomUIManager AddCloseButton(Action close) => throw new InvalidOperationException("unexpected warning UI");
    internal void CloseWindow(string id) => throw new InvalidOperationException("unexpected warning UI");
    internal void Show() => throw new InvalidOperationException("unexpected warning UI");
}

internal static class RenderHandler
{
    internal static class ColorPalette
    {
        internal static readonly object LightCyan = new();
        internal static readonly object LightBlue = new();
        internal static readonly object LightRed = new();
    }
}

internal static class LocHelper
{
    internal static string GetLocalizedMechanic(string key, params object[] args) => key;
    internal static string GetLocalizedMechanicOrDefault(string key, string fallback) => fallback;
}

namespace ProbablyStolen.DigitalStorage.Platform.Localization
{
    internal sealed class ModText
    {
        internal Func<string, string?>? Translation;
        internal string Get(string key, params object[] args) => Translation?.Invoke(key) ?? Default(key, args);

        private static string Default(string key, object[] args) => key == "workbench_info_count"
            ? $"{args[0]}/{args[1]}" : key is "network_tooltip_device_power" or
            "network_tooltip_total_power" or "module_refusal_typesexceedlimit" or "module_refusal_bytesexceedcapacity"
            ? $"{key}:{args[0]}" : key;
    }
}

namespace ProbablyStolen.DigitalStorage.Platform.Network
{
    using ProbablyStolen.DigitalStorage.Common.Domain.Network;

    internal sealed class GridNetworkPowerCoordinator
    {
        internal int Calls { get; private set; }
        internal bool Powered { get; set; }
        internal bool IsPowered(NativeGridSnapshot snapshot, Grid grid)
        {
            Calls++;
            return Powered;
        }
    }
}
