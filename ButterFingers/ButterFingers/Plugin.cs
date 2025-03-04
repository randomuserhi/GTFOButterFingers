using API;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace ButterFingers.BepInEx;

// TODO(randomuserhi): Refactor code - This entire code base is ugly as fuck
//                     - Refer to how pocket and key items are done as I should have a TriggerSlip method instead of Footstep etc...

[BepInPlugin(Module.GUID, Module.Name, Module.Version)]
public class Plugin : BasePlugin {
    public override void Load() {
        APILogger.Log("Plugin is loaded!");
        harmony = new Harmony(Module.GUID);
        harmony.PatchAll();

        APILogger.Log("Debug is " + (ConfigManager.Debug ? "Enabled" : "Disabled"));

        ClassInjector.RegisterTypeInIl2Cpp<CarryItem>();
        ClassInjector.RegisterTypeInIl2Cpp<ResourcePack>();
        ClassInjector.RegisterTypeInIl2Cpp<Consumable>();
        ClassInjector.RegisterTypeInIl2Cpp<PocketItem>();
        ClassInjector.RegisterTypeInIl2Cpp<KeyItem>();
    }

    private static Harmony? harmony;
}