using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Partition;

/// <summary>Registers the partition view, its mode and clear actions, and its global view size setting.</summary>
public static class PartitionTerminalFeature
{
    /// <summary>The partition overview view identifier.</summary>
    public const string TerminalName = "partition";

    /// <summary>The draft mode action identifier.</summary>
    public const string ModeActionName = "partition_mode";

    /// <summary>The action that clears every rule on the current cell.</summary>
    public const string ClearActionName = "partition_clear";

    /// <summary>The independent partition window size setting key.</summary>
    public const string SizeSettingName = "partition_view_size";

    /// <summary>The partition size setting values in cycle order.</summary>
    public static readonly Setting<TerminalSize> ViewSize = new(SizeSettingName,
        new[] { TerminalSize.Small, TerminalSize.Medium, TerminalSize.Tall, TerminalSize.Full },
        size =>
        {
            var name = size.ToString().ToLowerInvariant();
            return new ButtonAppearance("button_size_" + name, "terminal_button_size_tip",
                "terminal_size_" + name);
        });

    /// <summary>Creates an isolated, frozen registry for a partition window.</summary>
    public static TerminalRegistry CreateRegistry(IConfigManager config, Action<string> diagnostic,
        Func<object, object> createView, Func<TerminalContext, PartitionMode> readMode,
        Action<TerminalContext> toggleMode, Action<TerminalContext> clear)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        var registry = new TerminalRegistry(config, diagnostic, TerminalName);
        Register(registry, createView, readMode, toggleMode, clear);
        registry.Freeze();
        return registry;
    }

    /// <summary>Registers the partition view, mode, size, and view-scoped clear button.</summary>
    public static void Register(TerminalRegistry registry, Func<object, object> createView,
        Func<TerminalContext, PartitionMode> readMode, Action<TerminalContext> toggleMode,
        Action<TerminalContext> clear)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (createView == null) throw new ArgumentNullException(nameof(createView));
        if (readMode == null) throw new ArgumentNullException(nameof(readMode));
        if (toggleMode == null) throw new ArgumentNullException(nameof(toggleMode));
        if (clear == null) throw new ArgumentNullException(nameof(clear));

        registry.AddTerminal(new TerminalDefinition(TerminalName, 0, "partition_title", _ => true, createView));
        registry.AddAction(new TerminalAction(ModeActionName, 0, ActionPlacement.Rail, null, _ => true,
            context => new ButtonAppearance(readMode(context) == PartitionMode.Blacklist
                ? "button_blacklist" : "button_whitelist", "partition_mode_button",
                readMode(context) == PartitionMode.Blacklist ? "partition_mode_blacklist" : "partition_mode_whitelist"),
            toggleMode));
        registry.AddSetting(ViewSize, 1, ActionPlacement.Rail, null);
        registry.AddAction(new TerminalAction(ClearActionName, 2, ActionPlacement.Rail, TerminalName, _ => true,
            _ => new ButtonAppearance("button_clear", "partition_clear"), clear));
    }
}
