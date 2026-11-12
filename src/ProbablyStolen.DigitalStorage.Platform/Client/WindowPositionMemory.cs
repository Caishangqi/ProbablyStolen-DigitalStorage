using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

// Native windows already use the item's ID in the saved position dictionary.
// Custom windows use offset keys to keep their coordinates separate from the native module window.
internal static class WindowPositionMemory
{
    internal static void Restore(GameItem? owner, CustomUIWindow? window)
    {
        var positions = PlayerStore.Instance?.contentWindowPositions;
        if (owner == null || window == null || !window.IsAlive || positions == null) return;
        if (Key(owner.GetUniqueID()) is { } key && positions.TryGetValue(key, out var position))
            window.Rect.anchoredPosition = position;
    }

    // Custom windows bypass native close and save callbacks; record moves while they are open.
    internal static void Track(GameItem? owner, CustomUIWindow? window)
    {
        if (owner == null || window == null || !window.IsAlive) return;
        var recorder = window.root.GetComponent<WindowPositionRecorder>() ?? window.root.AddComponent<WindowPositionRecorder>();
        recorder.Initialize(owner, window.Rect);
    }

    internal static void Forget(int uniqueId)
    {
        if (Key(uniqueId) is { } key) PlayerStore.Instance?.contentWindowPositions?.Remove(key);
    }

    internal static void Record(GameItem owner, Vector2 position)
    {
        var store = PlayerStore.Instance;
        if (store == null || Key(owner.GetUniqueID()) is not { } key) return;
        store.contentWindowPositions ??= new System.Collections.Generic.Dictionary<int, Vector2>();
        store.contentWindowPositions[key] = position;
    }

    // Native items also use fixed negative IDs; negating an ID would collide with those keys.
    private static int? Key(int uniqueId) => uniqueId > 0 ? int.MinValue + uniqueId : null;
}

internal sealed class WindowPositionRecorder : MonoBehaviour
{
    private GameItem? _owner;
    private RectTransform? _window;
    private Vector2? _recorded;

    internal void Initialize(GameItem owner, RectTransform window)
    {
        _owner = owner;
        _window = window;
        _recorded = window.anchoredPosition;
    }

    private void LateUpdate()
    {
        if (_owner == null || _window == null) return;
        var position = _window.anchoredPosition;
        if (_recorded == position) return;
        _recorded = position;
        WindowPositionMemory.Record(_owner, position);
    }
}

/// <summary>Connects native item destruction to saved window position cleanup.</summary>
public static class WindowPositionEvents
{
    /// <summary>Removes the custom window position while preserving the native window's separate entry.</summary>
    public static void ItemDestroyed(GameItemElement item) => WindowPositionMemory.Forget(item.uniqueId);
}
