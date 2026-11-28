using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Client.Tooltips;
using ProbablyStolen.DigitalStorage.Api.Integration;

namespace ProbablyStolen.DigitalStorage.Compat.ItemDetails;

/// <summary>Optional obstacle integration for the Item Details overlay.</summary>
[IntegrationPlugin]
public sealed class ItemDetailsPlugin : IntegrationPlugin
{
    /// <inheritdoc />
    public override string PluginUid => "digital_storage:itemdetails";
    /// <inheritdoc />
    public override string RequiredModId => ItemDetailsTargets.ModId;
    /// <inheritdoc />
    public override IReadOnlyCollection<string> VerifiedVersions => ItemDetailsTargets.VerifiedVersions;
    /// <inheritdoc />
    public override string SettingSectionKey => "integration_itemdetails_section";
    /// <inheritdoc />
    public override string SettingLabelKey => "integration_itemdetails_label";
    /// <inheritdoc />
    public override string SettingTooltipKey => "integration_itemdetails_tooltip";

    /// <inheritdoc />
    public override bool CanEnableByDefault() => true;

    /// <inheritdoc />
    public override void Register(IIntegrationRegistration registration) => ItemDetailsTargets.Log = registration.Log;

    /// <inheritdoc />
    public override void OnRuntimeAvailable(IDigitalStorageRuntime runtime) => ItemDetailsTargets.Attach();
    /// <inheritdoc />
    public override void OnRuntimeUnavailable() => ItemDetailsTargets.Detach();
}

/// <summary>Item Details protocol names and the lifecycle scoped obstacle bridge.</summary>
internal static class ItemDetailsTargets
{
    internal const string ModId = "item_details";
    internal const string BadgeSlot = "item_details.tooltip_badge.v1";
    internal const string ContentsPanelName = "ItemDetailsContentsGrid";
    internal const string TypeBadgeName = "ItemDetailsTypeBadge";
    internal static IReadOnlyCollection<string> VerifiedVersions { get; } = Array.Empty<string>();
    private static IDisposable? _registration;
    private static ITooltipService? _service;
    private static UnityEngine.RectTransform? _cachedRoot;
    private static UnityEngine.RectTransform? _cachedContents;
    private static bool _warned;
    private static bool _disabled;

    internal static void Attach()
    {
        Detach();
        _warned = false;
        _disabled = false;
        try
        {
            _service = ModHelper.Lookup<ITooltipService>(TooltipServiceKeys.ModId, TooltipServiceKeys.ServiceKey);
            if (_service == null) throw new InvalidOperationException("the tooltip service is unavailable");
            _registration = _service.RegisterObstacle("item_details.badge", GetObstacles);
        }
        catch (Exception error)
        {
            WarnOnce(error);
            Detach();
            throw;
        }
    }

    internal static void Detach()
    {
        _registration?.Dispose();
        _registration = null;
        _service = null;
        _cachedRoot = null;
        _cachedContents = null;
    }

    private static void Disable(Exception error)
    {
        if (_disabled) return;
        _disabled = true;
        _registration?.Dispose();
        _registration = null;
        _service = null;
        _cachedRoot = null;
        _cachedContents = null;
        WarnOnce(error);
    }

    private static IReadOnlyList<UnityEngine.RectTransform> GetObstacles()
    {
        if (_disabled) return Array.Empty<UnityEngine.RectTransform>();
        var result = new List<UnityEngine.RectTransform>();
        try
        {
            if (AppDomain.CurrentDomain.GetData(BadgeSlot) is Func<UnityEngine.RectTransform?> badge)
            {
                var node = badge();
                if (node != null && node.gameObject.activeInHierarchy) result.Add(node);
            }

            var root = RenderHandler.current?.overlayRoot?.rectTransform;
            if (root == null) return result;
            if (!ReferenceEquals(root, _cachedRoot) || _cachedContents == null)
            {
                _cachedRoot = root;
                _cachedContents = root.GetComponentsInChildren<UnityEngine.RectTransform>(true)
                    .FirstOrDefault(node => node != null && node.name == ContentsPanelName);
            }
            if (_cachedContents != null && _cachedContents.gameObject.activeInHierarchy) result.Add(_cachedContents);
        }
        catch (Exception error)
        {
            Disable(error);
            return Array.Empty<UnityEngine.RectTransform>();
        }
        return result;
    }

    /// <summary>Gets or sets the compatibility log, set when the plugin registers.</summary>
    internal static IIntegrationLog? Log { get; set; }

    private static void WarnOnce(Exception error)
    {
        if (_warned) return;
        _warned = true;
        Log?.Warning("item_details_obstacle_disabled", $"Item Details tooltip obstacle disabled ({error.GetType().Name}).");
    }
}
