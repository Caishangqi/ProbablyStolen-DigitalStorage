using System;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Platform.Content;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>Forwards native hooks to the module bay service owned by the enabled composition.</summary>
public static class ModuleBayExpansionHooks
{
    private static DigitalStorageContentService? _service;
    private static readonly ConditionalWeakTable<GameItem, object> Kits = new();

    /// <summary>Gets the active module bay API, or null when the mod is stopped.</summary>
    public static IModuleBayApi? Api => _service;

    /// <summary>Gets whether a managed module bay operation must be refused because it runs off the main thread.</summary>
    public static bool RejectsThread => _service != null && !_service.IsMainThread;

    internal static IDisposable Attach(DigitalStorageContentService service)
    {
        if (_service != null) throw new InvalidOperationException("A module bay service is already attached.");
        _service = service ?? throw new ArgumentNullException(nameof(service));
        return new Registration(service);
    }

    /// <summary>Gets whether the native operation belongs to the enabled module bay service.</summary>
    public static bool IsManaged(GameItem? item) => _service?.IsManaged(item) == true;

    /// <summary>Restores a bound item's recorded layout without moving its module items.</summary>
    public static void Initialize(GameItem item)
    {
        if (IsManaged(item)) _service!.Initialize(item);
    }

    /// <summary>Checks the bound item's next stage without mutating it.</summary>
    public static bool CanExpand(GameItem item) => _service?.CanExpand(item).Succeeded == true;

    /// <summary>Applies one stage without consuming a tool or adding value.</summary>
    public static void Expand(GameItem item) => _service?.Expand(item);

    /// <summary>Wraps native tool rules while retaining the complete previous delegate chain for other targets.</summary>
    public static void ExtendKit(GameItem kit)
    {
        if (kit == null) return;
        Kits.GetValue(kit, _ => new object());
        var action = kit.onItemTargetItemFunc;
        kit.mayItemTargetItemFunc = TargetRules.OrWith(kit.mayItemTargetItemFunc, CanUseManagedKit);
        kit.canItemTargetItemFunc = TargetRules.OrWith(kit.canItemTargetItemFunc, CanUseManagedKit);
        kit.onItemTargetItemFunc = (source, target) =>
        {
            if (!IsManaged(target)) action?.Invoke(source, target);
            else if (!RejectsThread) _service!.TryUseKit(target, source);
        };
    }

    /// <summary>Refuses invalid managed kit operations even when another rule was appended with OR semantics.</summary>
    public static bool RefusesTarget(GameItem? source, GameItem? target)
    {
        if (source == null || target == null || !Kits.TryGetValue(source, out _) || !IsManaged(target)) return false;
        return RejectsThread || !_service!.CanUseKit(target, source);
    }

    private static bool CanUseManagedKit(GameItem source, GameItem target) =>
        !RejectsThread && source?.identifier == DigitalStorageContentService.KitId && target != null &&
        IsManaged(target) && !target.IsTag(MachineHelper.STANDARD_MACHINE_TAG) && _service!.CanUseKit(target, source);

    private sealed class Registration : IDisposable
    {
        private DigitalStorageContentService? _owned;
        internal Registration(DigitalStorageContentService service) => _owned = service;
        public void Dispose()
        {
            if (ReferenceEquals(_service, _owned)) _service = null;
            _owned = null;
        }
    }
}
