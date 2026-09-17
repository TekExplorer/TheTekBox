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
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using TheTekBox.TheTekBoxCode.Modifiers;
using TheTekBox.TheTekBoxCode.Nodes;

namespace TheTekBox.TheTekBoxCode.Patches;

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
    static bool AnimateRelicPrefix(RelicModel relic, Vector2? startPosition = null, Vector2? startScale = null)
    {
        if (!NoRelics.IsActive() || relic == null) return true;
        if (!Config.RelicShatterVfxEnabled) return false;

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

        if (NRun.Instance?.MerchantRoom is { } shop)
        {
            var merchantRelic = shop.Inventory._relicContainer?.GetChildren().OfType<NMerchantRelic>().Select(r => r._relicNode).FirstOrDefault(r => r?._model?.Id == relic.Id);
            if (merchantRelic != null)
            {
                NShatterVfx.ShatterRelicNode(merchantRelic);
                return false;
            }
        }

        if (NRun.Instance?.EventRoom is { _event: AncientEventModel } eventRoom)
        {
            // TODO: doesn't work
            var buttons = eventRoom?.Layout?.OptionButtons.ToList();
            var button = buttons?.FirstOrDefault(b => b.Option.Relic?.Id == relic.Id);
            var relicIcon = button?.GetNode<TextureRect>("%RelicIcon");
            if (relicIcon != null)
            {
                NShatterVfx.ShatterTextureRect(relicIcon);
                return false;
            }
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

        var nRelic = NRelic.Create(relic, NRelic.IconSize.Large);
        if (nRelic != null)
        {
            nRelic.GlobalPosition = startPosition ?? NGame.Instance!.Size / 2;
            if (startScale is { } s) nRelic.Scale = s;
            NOverlayStack.Instance?.AddChild(nRelic);
            NShatterVfx.ShatterRelicNode(nRelic);
        }

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
            NRun.Instance?.GlobalUi.RelicInventory.AnimateRelic(relic);
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