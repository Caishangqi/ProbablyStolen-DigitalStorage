using System;
using System.Linq;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Api;
using ProbablyStolen.DigitalStorage.Api.Content;
using ProbablyStolen.DigitalStorage.Api.Registries;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Platform.Api;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed partial class DigitalStorageContentService
{
    private readonly StorageDiagnostics? _storageDiagnostics;
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
            if (!Matches(bay, layout) && (SaveManager.IsDecoding || bay.childItems.Count == 0) && ChildrenFit(bay, layout))
            {
                bay.SetShape(string.Concat(layout.Rows), layout.Width);
                item.contentWindow?.Validate();
            }
            ReportLayoutMismatch(item, bay, session, stage, layout);
            return new ModuleBayState(true, stage, definition, null, maximum);
        }
        catch (Exception error)
        {
            ReportFailure(item, "module_bay_restore_failed", "layout restore failed", error, bay);
            return Unavailable(item, session, stage, definition, maximum, "layout restore failed: " + error.GetType().Name, bay);
        }
    }

    public ModuleBayState GetState(GameItem item) => GetState(item, out _);

    private ModuleBayState GetState(GameItem item, out GameGridInventory? inventory)
    {
        inventory = null;
        EnsureAlive();
        if (!_thread.IsMain) return new ModuleBayState(false, 0, null, "main thread required");
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!TryBinding(item, out var binding, out var definition))
            return new ModuleBayState(false, ReadStage(item), null, "module bay definition unavailable");
        var stage = ReadStage(item);
        var maximum = binding.MaximumStage ?? definition.Stages.Count - 1;
        var session = _baySessions.GetOrCreateValue(item);
        if (stage < 0 || stage > maximum)
            _storageDiagnostics?.Failure(item, "module_bay_invalid_stage", "Digital Storage: module bay has an invalid recorded stage.",
                () => NativeStorageDiagnosticContext.Item(item) + $" stage={stage} maximum={maximum}");
        if (stage < 0 || stage > maximum || session.Transaction.IsLocked || session.Failure != null)
            return new ModuleBayState(false, stage, definition, session.Failure ?? "invalid or locked module bay", maximum);
        try
        {
            if (!TryProvider(binding, item, out var provider, out var reason) || !provider.TryGetInventory(item, out var bay))
            {
                var refusal = reason ?? "module inventory unavailable";
                _storageDiagnostics?.Failure(item, "module_bay_unavailable", "Digital Storage: module provider or inventory is unavailable.",
                    () => NativeStorageDiagnosticContext.Item(item) + $" stage={stage} reason=provider_or_inventory_unavailable");
                return new ModuleBayState(false, stage, definition, refusal, maximum);
            }
            ReportLayoutMismatch(item, bay, session, stage, definition.Stage(stage).Layout);
            inventory = bay;
            return new ModuleBayState(true, stage, definition, null, maximum);
        }
        catch (Exception error)
        {
            ReportFailure(item, "module_bay_provider_failed", "module provider failed", error, null);
            return new ModuleBayState(false, stage, definition, "module provider failed: " + error.GetType().Name, maximum);
        }
    }

    public ModuleBayChangeResult CanExpand(GameItem item) => CanExpand(item, out _, out _);

    private ModuleBayChangeResult CanExpand(GameItem item, out ModuleBayState state, out GameGridInventory? bay)
    {
        state = GetState(item, out bay);
        if (!state.Available) return Failed(state, state.RefusalReason ?? "module bay unavailable");
        if (SaveManager.IsDecoding || !GeneralHelper.IsItemOwned(item)) return ModuleBayChangeResult.Refused(state.Stage, "decoding or item not owned");
        if (state.Stage >= state.MaximumStage) return ModuleBayChangeResult.Refused(state.Stage, "maximum stage");

        var nextStage = state.Stage + 1;
        if (!ChildrenFit(bay!, state.Definition!.Stage(nextStage).Layout))
            return ModuleBayChangeResult.Refused(state.Stage, "installed modules do not fit the next stage layout");
        return ModuleBayChangeResult.Changed(nextStage);
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
        var check = CanExpand(item, out var state, out var bay);
        if (!check.Succeeded) return check;
        var nextLayout = state.Definition!.Stage(check.Stage).Layout;
        var session = _baySessions.GetOrCreateValue(item);
        var port = new NativeModuleBayPort(item, bay!, kit, () => Notify(item, GetState(item)));
        var result = session.Transaction.Execute(port, state.Stage, state.MaximumStage, nextLayout, kit != null);
        if (session.Transaction.IsLocked)
        {
            session.Failure = result.Reason;
            bay!.LockInv();
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

    private void ReportLayoutMismatch(GameItem item, GameGridInventory bay, BaySession session, int stage, ModuleBayLayout layout)
    {
        if (Matches(bay, layout) && ChildrenFit(bay, layout)) return;

        // The decoder assigns the saved UID and stage after the item factory runs.
        var identity = (item.uniqueId, stage);
        if (session.ReportedLayoutMismatch == identity) return;
        session.ReportedLayoutMismatch = identity;
        StorageDiagnostics.Report(_diagnostic, "module_bay_layout_mismatch", "Digital Storage: module bay layout or module placement differs from the recorded stage.",
            context: () => NativeStorageDiagnosticContext.Item(item) + $" stage={stage} " +
                StorageDiagnostics.Field("actual", () => $"{bay.inventoryShape.width}x{bay.inventoryShape.height}") +
                $" expected={layout.Width}x{layout.Height} " + StorageDiagnostics.Field("modules", () => bay.childItems.Count));
    }

    private void Report(GameItem item, string reason)
    {
        try { _diagnostic($"module_bay item={item.identifier} result=unavailable reason={reason}"); }
        catch { }
    }

    private void ReportFailure(GameItem item, string eventCode, string message, Exception error, GameGridInventory? bay)
    {
        if (_storageDiagnostics == null)
        {
            StorageDiagnostics.Report(_diagnostic, eventCode, $"Digital Storage: {message}.", error,
                () => NativeStorageDiagnosticContext.Item(item));
            return;
        }

        _storageDiagnostics.Failure(item, eventCode, $"Digital Storage: {message}.", error,
            () => NativeStorageDiagnosticContext.Item(item) + " " + StorageDiagnostics.Field("stage", () => ReadStage(item)) +
                " " + StorageDiagnostics.Field("modules", () => bay?.childItems.Count));
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
        internal (int Uid, int Stage)? ReportedLayoutMismatch;
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
        private GridShape _originalShape = null!;
        private (GameItem Item, GridShape Shape)[] _originalModules = Array.Empty<(GameItem, GridShape)>();
        private bool _originalDynamicBackground;
        private TagState? _stage;
        private long _value;

        internal NativeModuleBayPort(GameItem item, GameGridInventory bay, GameItem? kit, Action notify)
        {
            _item = item;
            _bay = bay;
            _kit = kit;
            _notify = notify;
        }

        public void Capture()
        {
            _originalShape = new GridShapeBuilder(_bay.inventoryShape);
            _originalModules = _bay.childItems.Select(child => (child, (GridShape)new GridShapeBuilder(child.modifiedShape))).ToArray();
            _originalDynamicBackground = _bay.renderDynamicBackground;
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
            if (!MatchesOriginalModules() || !InstalledModulesFit())
                throw new InvalidOperationException("Module positions changed or no longer fit the applied module bay.");
        }
        public void AddValue() => _item.unitValue = checked(_item.unitValue + 100);
        public void Restore()
        {
            if (_stage == null) _item.state.dict.Remove(StageTag);
            else _item.state.dict[StageTag] = _stage.Clone();
            _item.unitValue = _value;
            _item.ValidateShapeState();
            if (!MatchesOriginalShape()) RestoreShape();
            _bay.Validate();
            _item.contentWindow?.Validate();
            if (!MatchesOriginalShape() || !MatchesOriginalModules())
                throw new InvalidOperationException("Module bay did not return to its original layout and placement.");
        }

        private bool InstalledModulesFit() => _bay.childItems.All(child =>
            child.modifiedShape.FullyIntersects(_bay.inventoryShape, static (itemCell, bayCell) => itemCell != 1 || bayCell == 0));

        private bool MatchesOriginalShape() => _bay.renderDynamicBackground == _originalDynamicBackground &&
            ShapesEqual(_bay.inventoryShape, _originalShape);

        private bool MatchesOriginalModules()
        {
            var children = _bay.childItems;
            if (children.Count != _originalModules.Length) return false;
            for (var i = 0; i < children.Count; i++)
            {
                var (item, shape) = _originalModules[i];
                if (!ReferenceEquals(children[i], item) || !ShapesEqual(item.modifiedShape, shape)) return false;
            }
            return true;
        }

        private static bool ShapesEqual(GridShape shape, GridShape original)
        {
            if (shape.width != original.width || shape.height != original.height ||
                shape.minX != original.minX || shape.minY != original.minY ||
                shape.orientation != original.orientation || shape.flipped != original.flipped ||
                shape.GetOutsideBounds() != original.GetOutsideBounds()) return false;
            for (var y = 0; y < shape.height; y++)
            for (var x = 0; x < shape.width; x++)
                if (shape.GetLocal(x, y) != original.GetLocal(x, y)) return false;
            return true;
        }

        private void RestoreShape()
        {
            var width = _originalShape.width;
            var height = _originalShape.height;
            var digits = new char[checked(width * height)];
            var rectangle = !_originalDynamicBackground;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var cell = _originalShape.GetLocal(x, y);
                digits[y * width + x] = (char)('0' + cell);
                rectangle &= cell == 0;
            }

            _bay.renderDynamicBackground = _originalDynamicBackground;
            if (rectangle) _bay.SetShape(width, height);
            else _bay.SetShape(new string(digits), width);
            // SetShape rebuilds an untransformed grid with the default outside-cell value.
            if (_bay.inventoryShape is GridShapeBuilder restored)
                restored.SetTransform(_originalShape.minX, _originalShape.minY, _originalShape.flipped, _originalShape.orientation)
                    .SetDataOutside(_originalShape.GetOutsideBounds());
            _bay.renderDynamicBackground = _originalDynamicBackground;
            if (_originalDynamicBackground && _originalShape.GetOutsideBounds() != 1)
                _bay.ForceRecomputeDynamicBackground();
        }

        public void Consume() => StartingPerkContent.ConsumeUpgradeMaterial(_kit!);
        public void Notify() => _notify();
    }
}
