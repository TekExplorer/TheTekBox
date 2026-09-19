using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using TheTekBox.TheTekBoxCode.Modifiers;
using TheTekBox.TheTekBoxCode.Nodes;
namespace TheTekBox.TheTekBoxCode.Patches;

[HarmonyPatch]
public static class NoRelicsPatch
{
    class IntBox(int value)
    {
        public int Value { get; set; } = value;
    }
    static readonly NotNullSpireField<Player, IntBox> AllowNextCmdRelics = new(() => new(0));
    static readonly NotNullSpireField<Player, List<RelicModel>> AllowedRelics = new(() => []);

    [HarmonyPatch(typeof(RelicConsoleCmd), nameof(RelicConsoleCmd.Process))]
    static class RelicConsoleCmdPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref CmdResult __result, Player? issuingPlayer, ref string[] args)
        {
            if (issuingPlayer == null || !NoRelics.IsActive(issuingPlayer)) return true;
            if (args.Length < 1) return true;
            if (args[0].ToLowerInvariant().Equals("remove")) return true;
            var allowedNext = AllowNextCmdRelics[issuingPlayer];
            if (args.Last().ToLowerInvariant().Equals("--force"))
            {
                allowedNext.Value++;
                return true;
            }
            if (args[0].ToLowerInvariant().Equals("allownext"))
            {
                int amount = 1;
                if (args.Length > 1 && !int.TryParse(args[1], out amount))
                {
                    __result = new(false, $"Invalid argument: {args[1]} is not an integer. Command was \"relic {string.Join(' ', args)}\"");
                    return false;
                }
                allowedNext.Value += amount;
                if (allowedNext.Value < 0) allowedNext.Value = 0;
                __result = new(true, allowedNext.Value switch
                {
                    0 => $"No relics will be allowed through the No Relics modifier.\nThank you for keeping to the challenge!",
                    1 => $"Your next relic will be allowed through the No Relics modifier.\nYou can clear this by running \"relic allownext -1\"",
                    _ => $"Your next {allowedNext.Value} relics will be allowed through the No Relics modifier.\nYou can clear this by running \"relic allownext {-allowedNext.Value}\""
                });
                return false;
            }
            if (allowedNext.Value > 0) return true;
            __result = new(false, "No Relics modifier is active.\nTo allow the next relic to be added, use \"relic allownext 1\". Negative values remove next allows.\nAdding --force to the end of your command will also bypass this check.");
            return false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic), [typeof(RelicModel), typeof(Vector2?), typeof(Vector2?)])]
    static bool AnimateRelicPrefix(RelicModel relic, Vector2? startPosition = null, Vector2? startScale = null)
    {
        if (!NoRelics.IsActive()) return true;
        if (AllowedRelics[LocalContext.GetMe(RunManager.Instance.State)!].Contains(relic)) return true;
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

    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Replace))]
    [HarmonyPrefix]
    static void Replace(RelicModel original, RelicModel replace)
    {
        AllowedRelics[original.Owner].Add(replace);
    }

    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Remove))]
    [HarmonyPrefix]
    static void Remove(RelicModel relic)
    {
        if (!NoRelics.IsActive()) return;
        if (!LocalContext.IsMine(relic)) return;
        var _relicNodes = NRun.Instance?.GlobalUi.RelicInventory._relicNodes;
        var nRelicInventoryHolder = _relicNodes?.FirstOrDefault(n => n.Relic.Model == relic);
        if (nRelicInventoryHolder == null) return;
        PlaySfx();
        NShatterVfx.ShatterRelicNode(nRelicInventoryHolder.Relic);
    }
    [HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)])]
    static class RelicCmdObtain
    {
        [HarmonyPostfix]
        static void Postfix(ref Task<RelicModel> __result, RelicModel relic, Player player)
        {
            // Ensure no race conditions. we decrement after.
            __result.ContinueWith(_ => AllowedRelics[player].Remove(relic));
        }

        [HarmonyPrefix]
        static bool Prefix(ref Task<RelicModel> __result, RelicModel relic, Player player)
        {
            if (!player.RunState.Modifiers.Any(mod => mod is NoRelics))
            {
                return true;
            }

            if (AllowedRelics[player].Contains(relic)) return true;
            if (AllowNextCmdRelics[player].Value > 0)
            {
                AllowedRelics[player].Add(relic);
                AllowNextCmdRelics[player].Value--;
                return true;
            }

            async Task<RelicModel> Do()
            {
                await DoEffect(relic, player);
                return relic;
            }

            __result = Do();
            return false;
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
                PlaySfx();
            }

            await Task.CompletedTask;
        }

    }
    private static void PlaySfx()
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
}