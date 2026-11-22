using System;
using System.Collections.Generic;
using UnityEngine;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Partition;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal static class WorkbenchCellSummary
{
    // Native slot and module callbacks must survive an unreadable cell; keep its name or notice visible.
    internal static string Render(GameItem? cell, string? notice, ModText text,
        Func<GameItem, CellModuleUsage?> findUsage, Action<Exception> reportError)
    {
        if (cell == null) return notice ?? text.Get("workbench_status_empty");
        var first = notice ?? cell.GetDisplayName(preferCustomName: true);
        try
        {
            var config = new CellPartitionTags(cell).Capture().Interpret().Config;
            var limits = CellTypeLimitSource.ReadOrBase(cell);
            var mode = text.Get(config.Mode == PartitionMode.Blacklist ? "partition_mode_blacklist" : "partition_mode_whitelist");
            var storedTypes = findUsage(cell) is { Status: CellModuleUsage.ReadStatus.Ready } usage
                ? usage.StoredTypes.ToString()
                : "?";
            return string.Join("\n", new List<string>
            {
                first,
                Line(text, "workbench_info_mode", mode),
                Line(text, "workbench_info_rules", text.Get("workbench_info_count", config.Rules.Count, limits.PartitionRuleLimit)),
                Line(text, "workbench_info_types", text.Get("workbench_info_count", storedTypes, limits.StorageTypeLimit))
            });
        }
        catch (Exception error)
        {
            reportError(error);
            return first;
        }
    }

    private static string Line(ModText text, string labelKey, string value) =>
        text.Get(labelKey) + ": <color=#" + ColorUtility.ToHtmlStringRGB(MachineryStyle.VALUE_TEXT) + ">" + value + "</color>";
}
