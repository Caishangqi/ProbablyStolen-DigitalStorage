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
        internal string Get(string key, params object[] args) => key;
    }
}

namespace ProbablyStolen.DigitalStorage.Platform.Network
{
    using ProbablyStolen.DigitalStorage.Common.Domain.Network;

    internal sealed class GridNetworkPowerCoordinator
    {
        internal int Calls { get; private set; }
        internal bool IsPowered(NativeGridSnapshot snapshot, Grid grid)
        {
            Calls++;
            return false;
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

namespace ProbablyStolen.DigitalStorage.Platform.Client
{
    internal static class ModuleChangeTooltipPresenter
    {
        internal static void AppendRemovalRefusal(RichTextBuilder builder, GameItem item,
            Localization.ModText text)
        {
            if (item.TooltipRefusal) builder.Lines.Add("module_refusal");
        }
    }
}
