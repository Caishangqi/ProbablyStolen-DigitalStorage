using System;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Platform.Api;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed partial class DigitalStorageContentService
{
    private readonly ApiThread _thread = new();
    private readonly ConditionalWeakTable<GameItem, BaySession> _baySessions = new();
    private readonly ConditionalWeakTable<GameItem, KitSession> _kitSessions = new();
    internal bool IsMainThread => _thread.IsMain;
    internal const string StageTag = "MODULE_UPGRADE_STAGE_INT";
    internal const string KitId = "module_bay_expansion_kit";

    /// <summary>
    /// Gets whether this service owns the item's module bay: only items with an exact binding published at the freeze,
    /// this mod's products included. Only the item identifier is read, so the answer is the same on every thread.
    /// </summary>
    internal bool IsManaged(GameItem? item) => item != null && IsManagedId(item.identifier);

    internal bool IsManagedId(string? identifier) =>
        identifier != null && _bindingsByItem?.ContainsKey(identifier) == true;

    internal int MaximumStage(GameItem item)
    {
        EnsureAlive();
        if (!_thread.IsMain || !TryBinding(item, out var binding, out var definition))
            throw new InvalidOperationException("Module bay definition is unavailable.");
        return binding.MaximumStage ?? definition.Stages.Count - 1;
    }

    public ModuleBayState Initialize(GameItem item)
    {
        EnsureAlive();
        if (!_thread.IsMain) return new ModuleBayState(false, 0, null, "main thread required");
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!TryBinding(item, out var binding, out var definition))
            return new ModuleBayState(false, ReadStage(item), null, "module bay definition unavailable");
        var stage = ReadStage(item);
        var session = _baySessions.GetOrCreateValue(item);
        var maximum = binding.MaximumStage ?? definition.Stages.Count - 1;
        if (session.Transaction.IsLocked || session.Failure != null)
            return new ModuleBayState(false, stage, definition, session.Failure ?? "module bay locked", maximum);
        // Resolved before any check so that every later failure can lock the bay it found.
        GameGridInventory? bay = null;
        try
        {
            var provider = _manager.Get<IModuleBayProvider>(Identifier.Parse(ProviderRegistryId)).GetRequired(binding.ProviderId);
            if (!provider.TryGetInventory(item, out var found))
                return Unavailable(item, session, stage, definition, maximum, "module inventory unavailable", null);
            bay = found;
            if (stage < 0 || stage > maximum)
                return Unavailable(item, session, stage, definition, maximum, "invalid recorded stage", bay);
            var layout = definition.Stage(stage).Layout;
            if (!Matches(bay, layout))
            {
                if (!SaveManager.IsDecoding && bay.childItems.Count > 0)
                    return Unavailable(item, session, stage, definition, maximum, "existing module layout differs from recorded stage", bay);
                if (!ChildrenFit(bay, layout))
                    return Unavailable(item, session, stage, definition, maximum, "module coordinates do not fit recorded stage", bay);
                bay.SetShape(string.Concat(layout.Rows), layout.Width);
                item.contentWindow?.Validate();
            }
            return new ModuleBayState(true, stage, definition, null, maximum);
        }
        catch (Exception error)
        {
            return Unavailable(item, session, stage, definition, maximum, "layout restore failed: " + error.GetType().Name, bay);
        }
    }

    public ModuleBayState GetState(GameItem item)
    {
        EnsureAlive();
        if (!_thread.IsMain) return new ModuleBayState(false, 0, null, "main thread required");
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!TryBinding(item, out var binding, out var definition))
            return new ModuleBayState(false, ReadStage(item), null, "module bay definition unavailable");
        var stage = ReadStage(item);
        var maximum = binding.MaximumStage ?? definition.Stages.Count - 1;
        var session = _baySessions.GetOrCreateValue(item);
        if (stage < 0 || stage > maximum || session.Transaction.IsLocked || session.Failure != null)
            return new ModuleBayState(false, stage, definition, session.Failure ?? "invalid or locked module bay", maximum);
        try
        {
            if (!TryProvider(binding, item, out var provider, out var reason) || !provider.TryGetInventory(item, out var bay))
                return new ModuleBayState(false, stage, definition, reason ?? "module inventory unavailable", maximum);
            if (!Matches(bay, definition.Stage(stage).Layout) || !ChildrenFit(bay, definition.Stage(stage).Layout))
                return Unavailable(item, session, stage, definition, maximum, "actual module layout does not match recorded stage", bay);
            return new ModuleBayState(true, stage, definition, null, maximum);
        }
        catch (Exception error)
        {
            return new ModuleBayState(false, stage, definition, "module provider failed: " + error.GetType().Name, maximum);
        }
    }

    public ModuleBayChangeResult CanExpand(GameItem item)
    {
        var state = GetState(item);
        if (!state.Available) return Failed(state, state.RefusalReason ?? "module bay unavailable");
        if (SaveManager.IsDecoding || !GeneralHelper.IsItemOwned(item)) return ModuleBayChangeResult.Refused(state.Stage, "decoding or item not owned");
        return state.Stage >= state.MaximumStage ? ModuleBayChangeResult.Refused(state.Stage, "maximum stage")
            : ModuleBayChangeResult.Changed(state.Stage + 1);
    }

    internal bool CanUseKit(GameItem machine, GameItem kit)
    {
        if (!_thread.IsMain || _disposed || kit == null || machine == null) return false;
        if (kit.identifier != KitId || kit.unitCount != 1 || !GeneralHelper.IsItemOwned(kit)) return false;
        var use = _kitSessions.GetOrCreateValue(kit);
        return !use.Active && !use.Uncertain && CanExpand(machine).Succeeded;
    }

    public ModuleBayChangeResult Expand(GameItem item) => ExecuteExpansion(item, null);

    public ModuleBayChangeResult TryUseKit(GameItem machine, GameItem kit)
    {
        EnsureAlive();
        if (!_thread.IsMain) return ModuleBayChangeResult.Failed(ModuleBayChangeStatus.Unavailable, 0, "main thread required");
        if (machine == null) throw new ArgumentNullException(nameof(machine));
        if (kit == null) throw new ArgumentNullException(nameof(kit));
        if (!CanUseKit(machine, kit)) return ModuleBayChangeResult.Refused(Math.Max(0, ReadStage(machine)), "kit or target unavailable");
        var use = _kitSessions.GetOrCreateValue(kit);
        use.Active = true;
        try
        {
            var result = ExecuteExpansion(machine, kit);
            if (result.Status == ModuleBayChangeStatus.ConsumptionUncertain) use.Uncertain = true;
            return result;
        }
        finally { use.Active = false; }
    }

    private ModuleBayChangeResult ExecuteExpansion(GameItem item, GameItem? kit)
    {
        var check = CanExpand(item);
        if (!check.Succeeded) return check;
        var state = GetState(item);
        if (!TryBinding(item, out var binding, out _) || !TryProvider(binding, item, out var provider, out _)
            || !provider.TryGetInventory(item, out var bay)) return Failed(state, "module inventory unavailable");
        if (!Matches(bay, state.Definition!.Stage(state.Stage).Layout) ||
            !ChildrenFit(bay, state.Definition.Stage(check.Stage).Layout))
            return Failed(state, "module coordinates or layout changed before expansion");
        var session = _baySessions.GetOrCreateValue(item);
        var port = new NativeModuleBayPort(item, bay, kit, () => Notify(item, GetState(item)));
        var result = session.Transaction.Execute(port, state.Stage, state.MaximumStage, state.Definition!.Stage(check.Stage).Layout, kit != null);
        if (session.Transaction.IsLocked)
        {
            session.Failure = result.Reason;
            bay.LockInv();
        }
        if (!result.Succeeded) Report(item, result.Reason ?? "module bay change failed");
        else if (kit != null)
        {
            try { EmporiumAudioPlayer.Instance.ratchet.Play(); }
            catch (Exception error) { Report(item, "sound failed: " + error.GetType().Name); }
        }
        return result;
    }

    private static ModuleBayChangeResult Failed(ModuleBayState state, string reason) =>
        ModuleBayChangeResult.Failed(ModuleBayChangeStatus.Unavailable, Math.Max(0, state.Stage), reason);

    /// <summary>Records the failure and locks the bay the item's provider returned; without one there is nothing to lock.</summary>
    private ModuleBayState Unavailable(GameItem item, BaySession session, int stage, ModuleBayDefinition definition, int maximum,
        string reason, GameGridInventory? bay)
    {
        session.Failure = reason;
        try { bay?.LockInv(); }
        catch { }
        Report(item, reason);
        return new ModuleBayState(false, stage, definition, reason, maximum);
    }

    private void Report(GameItem item, string reason)
    {
        try { _diagnostic($"module_bay item={item.identifier} result=unavailable reason={reason}"); }
        catch { }
    }

    private static int ReadStage(GameItem item) => item.modifiedState.dict.TryGetValue(StageTag, out var tag) ? tag.GetInt() : 0;

    private static bool Matches(GameGridInventory bay, ModuleBayLayout layout)
    {
        var shape = bay.inventoryShape;
        if (shape.width != layout.Width || shape.height != layout.Height) return false;
        for (var y = 0; y < layout.Height; y++)
        for (var x = 0; x < layout.Width; x++)
            if (shape.GetLocal(x, y) != (layout.IsOpen(x, y) ? 0 : 1)) return false;
        return true;
    }

    private static bool ChildrenFit(GameGridInventory bay, ModuleBayLayout layout)
    {
        foreach (var child in bay.childItems)
        {
            var shape = child.modifiedShape;
            for (var y = 0; y < shape.height; y++)
            for (var x = 0; x < shape.width; x++)
                if (shape.GetLocal(x, y) == 1)
                {
                    var coordinate = shape.ForwardTransform(x, y);
                    if (!layout.IsOpen(coordinate.Item1, coordinate.Item2)) return false;
                }
        }
        return true;
    }

    private sealed class BaySession
    {
        public BaySession() { }
        internal readonly ModuleBayTransaction Transaction = new();
        internal string? Failure;
    }

    private sealed class KitSession
    {
        public KitSession() { }
        internal bool Active;
        internal bool Uncertain;
    }

    private sealed class NativeModuleBayPort : IModuleBayTransactionPort
    {
        private readonly GameItem _item;
        private readonly GameGridInventory _bay;
        private readonly GameItem? _kit;
        private readonly Action _notify;
        private string _shape = "";
        private int _width;
        private TagState? _stage;
        private long _value;

        internal NativeModuleBayPort(GameItem item, GameGridInventory bay, GameItem? kit, Action notify)
        { _item = item; _bay = bay; _kit = kit; _notify = notify; }

        public void Capture()
        {
            var shape = _bay.inventoryShape;
            _width = shape.width;
            var digits = new char[checked(shape.width * shape.height)];
            for (var y = 0; y < shape.height; y++)
            for (var x = 0; x < shape.width; x++) digits[y * shape.width + x] = (char)('0' + shape.GetLocal(x, y));
            _shape = new string(digits);
            _stage = _item.state.dict.TryGetValue(StageTag, out var stage) ? stage.Clone() : null;
            _value = _item.unitValue;
        }
        public void Apply(ModuleBayLayout layout, int stage)
        {
            _bay.SetShape(string.Concat(layout.Rows), layout.Width);
            _item.ModifyTag(StageTag, tag => tag.Enable().SetInt(stage), skipValidate: true);
        }
        public void Validate()
        {
            _item.ValidateShapeState();
            _item.contentWindow?.Validate();
        }
        public void AddValue() => _item.unitValue = checked(_item.unitValue + 100);
        public void Restore()
        {
            if (_stage == null) _item.state.dict.Remove(StageTag);
            else _item.state.dict[StageTag] = _stage.Clone();
            _item.unitValue = _value;
            _item.ValidateShapeState();
            _bay.SetShape(_shape, _width);
            _item.contentWindow?.Validate();
        }
        public void Consume() => StartingPerkContent.ConsumeUpgradeMaterial(_kit!);
        public void Notify() => _notify();
    }
}
