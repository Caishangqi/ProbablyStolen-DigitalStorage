using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib;

internal static class AccessTools
{
    internal static MethodInfo? DeclaredMethod(string name, Type[] arguments) => throw new NotSupportedException();
    internal static MethodInfo? PropertySetter(Type type, string name) => throw new NotSupportedException();
    internal static MethodInfo? Method(Type type, string name) => type.GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

    internal static FieldInfo? Field(Type type, string name) => type.GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
}

internal sealed class CodeInstruction
{
    internal OpCode opcode;
    internal object? operand;
    internal readonly List<Label> labels = new();
    internal readonly List<object> blocks = new();

    internal CodeInstruction(OpCode opcode, object? operand = null)
    {
        this.opcode = opcode;
        this.operand = operand;
    }

    internal CodeInstruction(CodeInstruction instruction) : this(instruction.opcode, instruction.operand)
    {
        labels.AddRange(instruction.labels);
        blocks.AddRange(instruction.blocks);
    }
}

internal static class PatchProcessor
{
    internal static IEnumerable<KeyValuePair<OpCode, object>> ReadMethodBody(MethodBase method) => throw new NotSupportedException();
}
