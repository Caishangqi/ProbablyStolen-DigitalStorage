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

namespace ProbablyStolen.DigitalStorage.Platform.Storage
{
    internal sealed partial class BasicCellHandler
    {
        internal int TooltipLineCount { get; set; } = 1;
        internal BasicCellHandler(Localization.ModText text) { }
        internal void AddCellInformationToTooltip(RichTextBuilder builder, GameItem item, Func<bool>? isActive = null)
        {
            if (item.ThrowTooltip) throw new InvalidOperationException("injected cell failure");
            for (var index = 0; index < TooltipLineCount; index++)
            {
                if (isActive?.Invoke() == false) break;
                builder.AddLine(index == 0 ? "cell" : "cell_" + (index + 1), true, RenderHandler.ColorPalette.LightBlue);
            }
        }
    }
}
