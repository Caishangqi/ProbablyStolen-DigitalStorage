using System;

public sealed class GameItemElement : GameItem
{
    internal GameItemElement(string identifier) : base(identifier) { }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string methodName, Type[] arguments) { }
    }
}
