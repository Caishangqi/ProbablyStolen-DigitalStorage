using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib;

internal static class AccessTools
{
    internal static MethodInfo? DeclaredMethod(string name, Type[] arguments) => throw new NotSupportedException();
    internal static MethodInfo? PropertySetter(Type type, string name) => throw new NotSupportedException();
    internal static MethodInfo? Method(Type type, string name) => throw new NotSupportedException();
}

internal static class PatchProcessor
{
    internal static IEnumerable<KeyValuePair<OpCode, object>> ReadMethodBody(MethodBase method) => throw new NotSupportedException();
}
