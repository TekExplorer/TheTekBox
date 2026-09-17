using Godot;
using HarmonyLib;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using BaseLib.Extensions;
using System.Reflection;

namespace OneMaxHpModifier.OneMaxHpModifierCode;

//You're recommended but not required to keep all your code in this package and all your assets in the OneMaxHpModifier folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "OneMaxHpModifier"; //At the moment, this is used only for the Logger and harmony names.

    public static Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());

        Harmony harmony = new(ModId);

        harmony.TryPatchAll(Assembly.GetExecutingAssembly());

        Modifiers.Precarious.Preload();
    }
}
