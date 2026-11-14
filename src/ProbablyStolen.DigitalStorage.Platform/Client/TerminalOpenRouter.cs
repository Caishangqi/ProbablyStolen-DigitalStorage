using System;
using System.Runtime.CompilerServices;
using ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Routes opening a terminal to its terminal window instead of the native machinery window. The module bay is
/// reached through the terminal's modules page; nothing opens the native window on purpose.
/// </summary>
public static class TerminalOpenRouter
{
    private static readonly ConditionalWeakTable<GameItem, TerminalWindow> Windows = new();

    internal static void Register(GameItem terminal, TerminalWindow window)
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
        if (item == null || !Windows.TryGetValue(item, out var window)) return false;
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
}
