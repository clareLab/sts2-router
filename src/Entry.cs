using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace router;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public static void Initialize()
    {
        var harmony = new Harmony("clareLab.router");
        try
        {
            harmony.PatchAll(typeof(Entry).Assembly);
#if ROUTER_SELFTEST
            SelfTests.Initialize();
#endif
            GD.Print("[router] Loaded 0.1.0");
        }
        catch (Exception error)
        {
            harmony.UnpatchAll(harmony.Id);
            GD.PrintErr("[router] Disabled: " + error.Message);
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class MapPatch
{
    private static void Postfix(NMapScreen __instance)
    {
        try
        {
            var router = __instance.GetNodeOrNull<RouterControl>("Router");
            if (router == null)
            {
                router = new RouterControl { Name = "Router" };
                __instance.AddChild(router);
                router.Initialize();
            }
            router.Invalidate();
        }
        catch (Exception error) { GD.PrintErr("[router] Map overlay unavailable: " + error.Message); }
    }
}
