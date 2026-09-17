using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using OneMaxHpModifier.OneMaxHpModifierCode.Modifiers;
using OneMaxHpModifier.OneMaxHpModifierCode.Nodes;

namespace OneMaxHpModifier.OneMaxHpModifierCode.Patches;

[HarmonyPatch]
public static class NoRelicsPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(RelicConsoleCmd), nameof(RelicConsoleCmd.Process))]
    static bool MogCommandLine(ref CmdResult __result, Player? issuingPlayer)
    {
        if (issuingPlayer == null || !NoRelics.IsActive(issuingPlayer)) return true;
        __result = new(false, "No.");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic), [typeof(RelicModel), typeof(Vector2?), typeof(Vector2?)])]
    static bool AnimateRelicPrefix(RelicModel relic)
    {
        if (!NoRelics.IsActive() || relic == null) return true;

        // 1. CHESTS: Use the live holder's NRelic
        if (NRun.Instance?.TreasureRoom is { _relicCollection: { } collection })
        {
            var singleHolder = collection.SingleplayerRelicHolder;
            if (GodotObject.IsInstanceValid(singleHolder) && singleHolder.Visible)
            {
                NShatterVfx.ShatterRelicNode(singleHolder.Relic);
                return false;
            }

            var holders = collection._multiplayerHolders.Where(h => h.Visible);
            foreach (var holder in holders)
            {
                NShatterVfx.ShatterRelicNode(holder.Relic);
            }
            return false;
        }

        // 2. REWARDS SCREEN: Use the button's live _iconContainer
        if (NOverlayStack.Instance?.Peek() is NRewardsScreen screen)
        {
            var relicButton = screen._rewardButtons
                .OfType<NRewardButton>()
                .FirstOrDefault(b => b.Reward is RelicReward r && r.Relic?.Id == relic.Id);

            if (relicButton != null && GodotObject.IsInstanceValid(relicButton))
            {
                Control? container = relicButton._iconContainer;
                List<Node>? children = container?.GetChildren().ToList();
                //
                if (children?.OfType<TextureRect>().FirstOrDefault() is TextureRect icon)
                {
                    NShatterVfx.ShatterTextureRect(icon);
                    return false;
                }
            }
        }

        // event

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)])]
    static bool Prefix(ref Task<RelicModel> __result, RelicModel relic, Player player)
    {
        if (player.RunState.Modifiers.Any(mod => mod is NoRelics))
        {
            async Task<RelicModel> Do()
            {
                await DoEffect(relic, player);
                return relic;
            }

            __result = Do();
            return false;
        }
        return true;
    }

    private static async Task DoEffect(RelicModel relic, Player player)
    {
        relic.AssertMutable();
        IRunState runState = player.RunState;

        if (!relic.IsStackable)
        {
            player.RelicGrabBag.Remove(relic);
            runState.SharedRelicGrabBag.Remove(relic);
        }

        if (LocalContext.IsMe(player))
        {
            switch (Config.RelicGetSfx)
            {
                case Config.RelicGetSfxType.Normal:
                    NDebugAudioManager.Instance?.Play("relic_get.mp3");
                    break;
                case Config.RelicGetSfxType.Shatter:
                    NDebugAudioManager.Instance?.Play("universfield-bottle-shatter-229205.mp3");
                    break;
            }
        }

        await Task.CompletedTask;
    }
}