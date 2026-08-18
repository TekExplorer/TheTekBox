using System.Reflection.Emit;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using OneMaxHpModifier.OneMaxHpModifierCode.Patches;

namespace OneMaxHpModifier.OneMaxHpModifierCode.Modifiers;

// [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp), MethodType.Async)]
// static class MaxHpPatch
// {
//     [HarmonyTranspiler]
//     static internal IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, IEnumerable<CodeInstruction> code, MethodBase original)
//     {
//         return AsyncMethodCall.Create(generator, code, original,
//             callMethod: AccessTools.Method(typeof(MaxHpPatch), nameof(FixMaxHp)),
//             afterState: original
//         );
//     }

//     static internal async Task FixMaxHp(Creature creature)
//     {
//         if (!creature.IsPlayer) return;
//         var player = creature.Player!;
//         if (!player.RunState.Modifiers.Any(mod => mod is Precarious)) return;
//         await Precarious.LoseMaxHpToOne(creature);
//     }
// }

public class Precarious : CustomModifierModel
{
    public override ModifierAlignment Alignment => ModifierAlignment.Bad;
    protected override string IconPath => ImageHelperExtensions.GetModImagePath("modifiers/1hp.png");

    public override Func<Task>? GenerateNeowOption(EventModel eventModel)
    {
        return () => LoseMaxHpToOne(eventModel.Owner.Creature);
    }

    internal static Task LoseMaxHpToOne(Creature creature)
    {
        return CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), creature, creature.MaxHp - 1, isFromCard: false);
    }
    protected override void AfterRunCreated(RunState runState)
    {
        runState.Players.Do(RemoveRelics);
    }

    static void RemoveRelics(Player player)
    {
        // player.RelicGrabBag.Remove<NutritiousOyster>();  // modify?
        // player.RelicGrabBag.Remove<StoneHumidifier>();
        // // remove byrd egg eats
        // // Big mushroom
        // // Mango??? // event
        // // chosen cheese
        // // Darkstone Periapt
        // // Sere Talon

        // player.RelicGrabBag.Remove<DragonFruit>();

        // player.RelicGrabBag.Remove<Mango>();
        // player.RelicGrabBag.Remove<Pear>();
        // player.RelicGrabBag.Remove<Strawberry>();
    }
}