using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace TheTekBox.TheTekBoxCode.Modifiers;

public class NoRelics : CustomModifierModel
{
    static public bool IsActive() => IsActive(RunManager.Instance?.State);
    static public bool IsActive(Player? player) => IsActive(player?.RunState);
    static public bool IsActive(IRunState? runState) => runState?.Modifiers.Any(m => m is NoRelics) == true;
    public override ModifierAlignment Alignment => ModifierAlignment.Bad;
    protected override string IconPath => ModelDb.Relic<Circlet>().IconPath;
    public override IEnumerable<ModifierModel> MutuallyExclusiveGroup => [ModelDb.Modifier<NeowOptions>()];
}
