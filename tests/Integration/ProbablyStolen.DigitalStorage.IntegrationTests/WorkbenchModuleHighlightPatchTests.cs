using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Mod.Patches;
using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
internal sealed class WorkbenchModuleHighlightPatchTests
{
    [Test]
    public void Transpiler_ReplacesOnlyHighlightProbeAndPreservesBranchTarget()
    {
        var input = HighlightProbe();
        input[1].labels.Add(default);
        var output = WorkbenchModuleHighlightPatch.Transpiler(input).ToArray();

        Assert.That(output.Select(instruction => instruction.opcode), Is.EqualTo(new[]
        {
            OpCodes.Ldloc_0, OpCodes.Ldarg_0, OpCodes.Ldfld, OpCodes.Call,
            OpCodes.Brfalse_S, OpCodes.Ldloc_0, OpCodes.Callvirt
        }));
        Assert.That(output[1].labels, Is.EqualTo(input[1].labels));
        Assert.That(output[2].operand, Is.EqualTo(AccessTools.Field(typeof(ItemMouseHandler), "currentItem")));
        Assert.That(output[3].operand, Is.EqualTo(AccessTools.Method(typeof(WorkbenchModuleHighlight),
            nameof(WorkbenchModuleHighlight.MayRemove))));
        Assert.That(output[4..], Is.EqualTo(input[2..]));
    }

    [Test]
    public void Transpiler_LeavesUnknownAndAmbiguousInstructionStreamsAlone()
    {
        var unexpected = HighlightProbe();
        unexpected[2] = new CodeInstruction(OpCodes.Brtrue_S);
        Assert.That(WorkbenchModuleHighlightPatch.Transpiler(unexpected), Is.EqualTo(unexpected));
        var ambiguous = HighlightProbe().Concat(HighlightProbe()).ToArray();
        Assert.That(WorkbenchModuleHighlightPatch.Transpiler(ambiguous), Is.EqualTo(ambiguous));
    }

    private static CodeInstruction[] HighlightProbe() => new[]
    {
        new CodeInstruction(OpCodes.Ldloc_0),
        new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(GameItem), nameof(GameItem.MayRemove))),
        new CodeInstruction(OpCodes.Brfalse_S, default(Label)),
        new CodeInstruction(OpCodes.Ldloc_0),
        new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(GameItem), nameof(GameItem.MaxNumRemove)))
    };
}
