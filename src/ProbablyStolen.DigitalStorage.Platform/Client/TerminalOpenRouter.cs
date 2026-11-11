using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Routes opening a terminal to its storage window instead of the native machinery window, where the
/// terminal screen is the only screen. The native window, which holds the module bay, opens only on request.
/// </summary>
public static class TerminalOpenRouter
{
    private static readonly ConditionalWeakTable<GameItem, TerminalStorageWindow> Windows = new();
    private static bool _nativeRequested;

    /// <summary>Gets whether the current native open was requested through <see cref="OpenNative"/>.</summary>
    internal static bool IsNativeRequested => _nativeRequested;

    internal static void Register(GameItem terminal, TerminalStorageWindow window)
    {
        Windows.Remove(terminal);
        Windows.Add(terminal, window);
    }

    /// <summary>
    /// Called before <c>ItemMouseDoubleClickHandler.OpenContentAction</c>, which double-click, single-click interact
    /// and the context-menu Open all use. Returns whether the terminal window handled it.
    /// </summary>
    public static bool TryOpen(GameItem? item)
    {
        if (_nativeRequested || item == null || !Windows.TryGetValue(item, out var window)) return false;
        try
        {
            window.Toggle();
            return true;
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogError($"Digital Storage: terminal window failed to open ({error.GetType().Name}); opening the native window");
            return false;
        }
    }

    /// <summary>Opens the terminal's native machinery window, with the game's own title, position and sound.</summary>
    internal static void OpenNative(GameItem terminal)
    {
        var handler = ItemMouseDoubleClickHandler.current;
        if (handler == null || terminal.contentWindow == null) return;
        _nativeRequested = true;
        try
        {
            handler.OpenContentAction(terminal, Input.mousePosition);
        }
        finally
        {
            _nativeRequested = false;
        }
    }
}
