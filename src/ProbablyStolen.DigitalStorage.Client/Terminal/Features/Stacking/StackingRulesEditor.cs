using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// The state behind the rules page. Every edit builds a complete candidate and saves it at once; a refused save keeps
/// the previous settings and reports why. Unreadable settings disable every edit, so the page never overwrites them.
/// </summary>
public sealed class StackingRulesEditor
{
    /// <summary>The message shown when a save is refused.</summary>
    public const string SaveFailedKey = "stacking_save_failed";

    /// <summary>The message key shown when a marker color is not #RGB or #RRGGBB.</summary>
    public const string MarkerInvalidKey = "stacking_marker_invalid";

    private readonly StackingConfigStore _store;
    private readonly Action _saved;

    /// <summary>Creates an editor over a terminal's settings.</summary>
    /// <param name="store">The terminal's settings.</param>
    /// <param name="saved">Runs after every successful save, for example to regroup the storage view.</param>
    public StackingRulesEditor(StackingConfigStore store, Action saved)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _saved = saved ?? throw new ArgumentNullException(nameof(saved));
    }

    /// <summary>Gets whether the saved settings can be read; edits are refused otherwise.</summary>
    public bool IsEditable => _store.Read().Status != StackingConfigStatus.Unreadable;

    /// <summary>Gets the settings shown on the page; the default while unreadable.</summary>
    public StackingConfig Config => _store.Effective() ?? StackingConfig.Default;

    /// <summary>Gets the message of the last refused save, cleared by the next successful one.</summary>
    public string? LastError { get; private set; }

    /// <summary>Lists the attribute menu for the items the terminal stores now.</summary>
    public IReadOnlyList<StackingFieldOption> MenuOptions(IEnumerable<ItemKey> storedKeys) =>
        StackingFieldCatalog.Discover(storedKeys, Config.Rules.Select(rule => rule.FieldId));

    /// <summary>
    /// Adds a row set to ignore, or finds the row the field already has. Returns the row index, or -1 when the save was
    /// refused.
    /// </summary>
    public int AddOrLocate(string fieldId)
    {
        var existing = Config.IndexOf(fieldId);
        if (existing >= 0) return existing;
        return Commit(Config.WithAddedRule(fieldId, StackingMode.Ignore)) ? Config.IndexOf(fieldId) : -1;
    }

    /// <summary>
    /// Changes a row's field. When another row already has the new field, nothing changes and that row's index is
    /// returned, so two rows are never merged or overwritten.
    /// </summary>
    public int ReplaceField(int row, string fieldId)
    {
        var existing = Config.IndexOf(fieldId);
        if (existing >= 0) return existing;
        return Commit(Config.WithField(row, fieldId)) ? row : -1;
    }

    /// <summary>Changes a row's mode.</summary>
    public bool SetMode(int row, StackingMode mode) => Commit(Config.WithMode(row, mode));

    /// <summary>Removes a row; the field goes back to exact comparison.</summary>
    public bool Remove(int row) => Commit(Config.WithoutRule(row));

    /// <summary>Removes every row.</summary>
    public bool Clear() => Commit(Config.WithoutRules());

    /// <summary>Switches merged display without touching the rows.</summary>
    public bool SetEnabled(bool enabled) => Commit(Config.WithEnabled(enabled));

    /// <summary>Sets the marker color from #RGB or #RRGGBB; other text keeps the current color.</summary>
    public bool SetMarkerColor(string? text)
    {
        if (!StackingMarker.TryParse(text, out var marker))
        {
            LastError = MarkerInvalidKey;
            return false;
        }

        return marker.Equals(Config.Marker) ? ClearError() : Commit(Config.WithMarker(marker));
    }

    private bool ClearError()
    {
        LastError = null;
        return true;
    }

    private bool Commit(StackingConfig candidate)
    {
        if (!IsEditable || !_store.TryCommit(candidate))
        {
            LastError = SaveFailedKey;
            return false;
        }

        LastError = null;
        _saved();
        return true;
    }
}
