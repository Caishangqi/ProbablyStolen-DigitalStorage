using System;

public sealed class GameItemElement : GameItem
{
    internal GameItemElement(string identifier) : base(identifier) { }
    internal bool IsDestroyed() => false;
    internal bool MayHaveValidInventorySlot(GameItem item) => throw new InvalidOperationException("unexpected container search");
    internal SlotMarker? TryFindOneValidInventorySlot(GameItem item) => throw new InvalidOperationException("unexpected container search");
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string methodName, Type[] arguments) { }
    }
}
