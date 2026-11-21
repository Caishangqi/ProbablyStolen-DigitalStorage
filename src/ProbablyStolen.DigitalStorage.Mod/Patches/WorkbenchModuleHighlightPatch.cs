using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Mod.Patches;

[HarmonyPatch(typeof(ItemMouseHandler), "OnEventUpdate", new Type[] { })]
internal static class WorkbenchModuleHighlightPatch
{
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var remove = AccessTools.Method(typeof(GameItem), nameof(GameItem.MayRemove));
        var maxRemove = AccessTools.Method(typeof(GameItem), nameof(GameItem.MaxNumRemove));
        var pressed = AccessTools.Field(typeof(ItemMouseHandler), "currentItem");
        var replacement = AccessTools.Method(typeof(WorkbenchModuleHighlight), nameof(WorkbenchModuleHighlight.MayRemove));
        if (remove == null || maxRemove == null || pressed == null || pressed.FieldType != typeof(GameItem) || replacement == null)
            return code;

        var index = -1;
        for (var i = 0; i < code.Count; i++)
        {
            if (!Equals(code[i].operand, remove)) continue;
            if (index >= 0) return code;
            index = i;
        }

        // Leave other IL shapes untouched. The intervening branch also skips MaxNumRemove for passive highlights.
        if (index < 1 || index + 3 >= code.Count || code[index].opcode != OpCodes.Callvirt || code[index].blocks.Count != 0 ||
            (code[index + 1].opcode != OpCodes.Brfalse_S && code[index + 1].opcode != OpCodes.Brfalse) ||
            code[index - 1].opcode != OpCodes.Ldloc_0 || code[index + 2].opcode != OpCodes.Ldloc_0 ||
            code[index + 3].opcode != OpCodes.Callvirt || !Equals(code[index + 3].operand, maxRemove)) return code;

        code[index] = new CodeInstruction(code[index]) { opcode = OpCodes.Ldarg_0, operand = null };
        code.Insert(index + 1, new CodeInstruction(OpCodes.Ldfld, pressed));
        code.Insert(index + 2, new CodeInstruction(OpCodes.Call, replacement));
        return code;
    }
}
