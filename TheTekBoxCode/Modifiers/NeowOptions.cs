using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using TheTekBox.TheTekBoxCode.Patches;

namespace TheTekBox.TheTekBoxCode.Modifiers;

// Marker for the patches that use it
public class NeowOptions : CustomModifierModel, IEnablesNeowBlessings
{
    public override ModifierAlignment Alignment => ModifierAlignment.Good;

    protected override string IconPath => ImageHelper.GetRoomIconPath(MapPointType.Ancient, RoomType.Event, ModelDb.AncientEvent<Neow>().Id)!;
}