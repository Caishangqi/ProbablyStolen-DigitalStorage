using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace ProbablyStolen.DigitalStorage.Compat.BrewingExpansion;

internal static class BrewingTooltipTarget
{
    internal static bool HasExpectedSignature(MethodInfo? target)
    {
        if (target is not { IsStatic: true, IsGenericMethod: false } || target.ReturnType != typeof(void)) return false;
        var parameters = target.GetParameters();
        return parameters.Length == 2 && parameters[0].ParameterType == typeof(RichTextBuilder) &&
            parameters[1].ParameterType == typeof(GameItem);
    }

    internal static bool HasLoreWrites(IEnumerable<KeyValuePair<OpCode, object>> instructions,
        MethodInfo descriptionSetter, MethodInfo flavorSetter)
    {
        var descriptionFound = false;
        var flavorFound = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Key != OpCodes.Call && instruction.Key != OpCodes.Callvirt) continue;
            descriptionFound |= descriptionSetter.Equals(instruction.Value);
            flavorFound |= flavorSetter.Equals(instruction.Value);
            if (descriptionFound && flavorFound) return true;
        }

        return false;
    }
}
