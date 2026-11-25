using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Host;

/// <summary>
/// Where a button applies: every terminal view, one terminal view, or one sub-screen while it is the top page.
/// View-level buttons never show on a sub-screen; a page that wants buttons names its own screen id.
/// </summary>
public sealed class ActionScope : IEquatable<ActionScope>
{
    private enum ScopeKind { AllViews, View, Screen }

    private readonly ScopeKind _kind;
    private readonly string? _name;

    private ActionScope(ScopeKind kind, string? name)
    {
        _kind = kind;
        _name = name;
    }

    /// <summary>Gets the scope of every terminal view, without sub-screens.</summary>
    public static ActionScope AllViews { get; } = new(ScopeKind.AllViews, null);

    /// <summary>Gets whether the scope is shared by every view; such buttons sort before scoped ones.</summary>
    public bool IsShared => _kind == ScopeKind.AllViews;

    /// <summary>Creates the scope of one terminal view.</summary>
    public static ActionScope View(string terminalName) =>
        new(ScopeKind.View, string.IsNullOrEmpty(terminalName)
            ? throw new ArgumentException("A terminal name is required.", nameof(terminalName)) : terminalName);

    /// <summary>Creates the scope of one sub-screen.</summary>
    public static ActionScope Screen(string screenId) =>
        new(ScopeKind.Screen, string.IsNullOrEmpty(screenId)
            ? throw new ArgumentException("A screen id is required.", nameof(screenId)) : screenId);

    /// <summary>Whether a button of this scope applies in <paramref name="context"/>.</summary>
    public bool Includes(TerminalContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        return _kind switch
        {
            ScopeKind.AllViews => context.ScreenId == null,
            ScopeKind.View => context.ScreenId == null && context.CurrentTerminal == _name,
            _ => context.ScreenId == _name
        };
    }

    /// <inheritdoc />
    public bool Equals(ActionScope? other) => other != null && _kind == other._kind && _name == other._name;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ActionScope);

    /// <inheritdoc />
    public override int GetHashCode() => (int)_kind * 397 ^ (_name == null ? 0 : StringComparer.Ordinal.GetHashCode(_name));

    /// <inheritdoc />
    public override string ToString() => _name == null ? _kind.ToString() : _kind + ":" + _name;
}
