using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace OneMaxHpModifier.OneMaxHpModifierCode.Patches;

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
public static class NeowGenerateInitialOptionsTranspiler
{

    public static bool ForceStandardBranch { get; set; } = false;

    // Replaces the evaluation of base.Owner.RunState.Modifiers.Count
    public static int GetEffectiveModifierCount(Neow instance)
    {
        if (ForceStandardBranch)
            return 0;

        return instance.Owner?.RunState.Modifiers.Count ?? 0;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        // Find the PropertyGetter for Modifiers.Count
        var countGetter = AccessTools.PropertyGetter(typeof(IReadOnlyCollection<ModifierModel>), nameof(IReadOnlyCollection<ModifierModel>.Count))
            ?? AccessTools.PropertyGetter(typeof(List<ModifierModel>), nameof(List<ModifierModel>.Count));

        // Locate get_Count()
        matcher.MatchStartForward(
            new CodeMatch(OpCodes.Callvirt, countGetter)
        );

        if (!matcher.IsValid)
        {
            matcher.Start();
            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Call, countGetter)
            );
        }

        if (matcher.IsValid)
        {
            int endPos = matcher.Pos;

            // Search back to the ldarg.0 that loaded 'this'
            matcher.MatchStartBackwards(new CodeMatch(OpCodes.Ldarg_0));
            int startPos = matcher.Pos;

            // Remove instructions: ldarg.0 -> Owner -> RunState -> Modifiers -> get_Count
            matcher.RemoveInstructions(endPos - startPos + 1);

            // Insert: ldarg.0 -> call GetEffectiveModifierCount(this)
            matcher.Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.Call(() => GetEffectiveModifierCount(null!))
            );
        }

        return matcher.InstructionEnumeration();
    }
}