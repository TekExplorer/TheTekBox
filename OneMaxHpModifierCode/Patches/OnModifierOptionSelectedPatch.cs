using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using OneMaxHpModifier.OneMaxHpModifierCode.Modifiers;

namespace OneMaxHpModifier.OneMaxHpModifierCode.Patches;

[HarmonyPatch(typeof(Neow), "OnModifierOptionSelected", MethodType.Async)]
public static class OnModifierOptionSelectedPatch
{
    private static bool ShouldAllowStandard(IEnumerable<ModifierModel> modifiers)
    {
        // TODO: make custom NeowOptions modifier
        return modifiers.Any(mod => mod is Precarious);
    }
    // Helper replacing SetEventFinished
    public static void HandleModifierFinished(Neow instance, LocString finishDescription)
    {
        var modifiers = instance.Owner?.RunState?.Modifiers;
        bool shouldContinue = modifiers != null && modifiers.Count > 0 && ShouldAllowStandard(modifiers);

        var trv = Traverse.Create(instance);

        if (!shouldContinue)
        {
            // Vanilla behavior: call base EventModel.SetEventFinished(LocString)
            trv.Method("SetEventFinished", [typeof(LocString)])
               .GetValue(finishDescription);
            return;
        }

        IReadOnlyList<EventOption> standardOptions;

        NeowGenerateInitialOptionsTranspiler.ForceStandardBranch = true;
        try
        {
            // Invoke protected GenerateInitialOptions via Traverse
            standardOptions = trv.Method("GenerateInitialOptions")
                                 .GetValue<IReadOnlyList<EventOption>>();
        }
        finally
        {
            NeowGenerateInitialOptionsTranspiler.ForceStandardBranch = false;
        }

        // Retrieve protected InitialDescription
        LocString initialDesc = trv.Property<LocString>("InitialDescription").Value;

        // Transition to standard 3 options via EventModel.SetEventState(LocString, IEnumerable<EventOption>)
        trv.Method("SetEventState", [typeof(LocString), typeof(IEnumerable<EventOption>)])
           .GetValue(initialDesc, standardOptions);
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);

        // Find the call to SetEventFinished(LocString) declared on EventModel / Neow
        var setEventFinishedMethod = AccessTools.Method(typeof(EventModel), "SetEventFinished", [typeof(LocString)])
            ?? AccessTools.Method(typeof(Neow), "SetEventFinished", [typeof(LocString)]);

        matcher.MatchStartForward(new CodeMatch(OpCodes.Callvirt, setEventFinishedMethod));
        if (!matcher.IsValid)
        {
            matcher.Start();
            matcher.MatchStartForward(new CodeMatch(OpCodes.Call, setEventFinishedMethod));
        }

        if (matcher.IsValid)
        {
            // Stack at this point: [Neow instance, LocString finishDescription]
            // Replace the method token directly with our static hook
            matcher.Set(
                OpCodes.Call,
                CodeInstruction.Call(() => HandleModifierFinished(null!, default!)).operand
            );
        }

        return matcher.InstructionEnumeration();
    }
}