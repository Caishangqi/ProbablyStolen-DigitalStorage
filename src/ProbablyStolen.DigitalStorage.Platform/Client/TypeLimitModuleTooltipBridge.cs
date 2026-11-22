using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;
using ProbablyStolen.DigitalStorage.Platform.Content;
using ProbablyStolen.DigitalStorage.Platform.Localization;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Projects a type limit roll into the native tooltip's transient tag view.</summary>
public static class TypeLimitModuleTooltipBridge
{
    private static Registration? _registration;

    internal static IDisposable Register(ModText text, Action<string> reportError)
    {
        var registration = new Registration(text, reportError);
        _registration = registration;
        return registration;
    }

    /// <summary>Replaces the legacy line for one synchronous native tooltip call.</summary>
    public static IDisposable? Begin(GameItem item)
    {
        var registration = _registration;
        if (registration == null || item == null || item.identifier != CellUpgradeInventory.TypeLimitId) return null;

        TypeLimitRoll? roll = null;
        try { roll = ModuleTags.Read(item); }
        catch (Exception error) { registration.Report(error); }
        var effect = roll != null && TypeLimitRolls.IsValid(roll)
            ? TypeLimitModuleText.Effect(registration.Text, roll, roll.EffectiveAdd, registration.Report)
            : TypeLimitModuleText.Invalid(registration.Text, registration.Report);

        // Localization and diagnostics may call other code. Project over the view still active after they return.
        var original = item.modifiedState;
        TagSystem projected;
        const string effectTag = ModuleHelper.ADDITIONAL_EFFECT_STRING;
        try
        {
            projected = original.Clone();
            // The clone shares TagState instances. Drop the old entry so only a new entry can be written.
            projected.dict.Remove(effectTag);
        }
        catch (Exception error)
        {
            registration.Report(error);
            return null;
        }

        TagState? injected = null;
        try
        {
            injected = projected.GetTag(effectTag);
            injected.Enable().SetString(effect);
        }
        catch (Exception error)
        {
            // If projection itself fails, don't show the stale raw bonus.
            projected.dict.Remove(effectTag);
            injected = null;
            registration.Report(error);
        }
        if (!ReferenceEquals(item.modifiedState, original)) return null;
        var scope = new Scope(item, original, projected, injected);
        item.modifiedState = projected;
        return scope;
    }

    private sealed class Registration : IDisposable
    {
        internal readonly ModText Text;
        private readonly Action<string> _reportError;
        private bool _reported;

        internal Registration(ModText text, Action<string> reportError)
        {
            Text = text;
            _reportError = reportError;
        }

        internal void Report(Exception error)
        {
            if (_reported) return;
            _reported = true;
            try { _reportError($"Digital Storage: type limit module tooltip projection failed ({error.GetType().Name})."); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (ReferenceEquals(_registration, this)) _registration = null;
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly GameItem _item;
        private readonly TagSystem _original;
        private readonly TagSystem _projected;
        private readonly TagState? _injected;
        private bool _disposed;

        internal Scope(GameItem item, TagSystem original, TagSystem projected, TagState? injected)
        {
            _item = item;
            _original = original;
            _projected = projected;
            _injected = injected;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var current = _item.modifiedState;
            if (ReferenceEquals(current, _projected))
            {
                _item.modifiedState = _original;
                return;
            }

            // A shallow clone made during the tooltip can still carry our entry. Keep the replacement
            // dictionary and unrelated edits, and only restore the entry if it is still ours.
            const string effectTag = ModuleHelper.ADDITIONAL_EFFECT_STRING;
            if (current == null || _injected == null || !current.dict.TryGetValue(effectTag, out var effect) ||
                !ReferenceEquals(effect, _injected)) return;
            if (_original.dict.TryGetValue(effectTag, out var previous)) current.dict[effectTag] = previous;
            else current.dict.Remove(effectTag);
        }
    }
}
