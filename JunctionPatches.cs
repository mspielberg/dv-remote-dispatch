using HarmonyLib;
using System;

namespace DvMod.RemoteDispatch
{
    public static class JunctionPatches
    {
        [HarmonyPatch(typeof(Junction), nameof(Junction.Switch), new Type[] { typeof(Junction.SwitchMode), typeof(byte) })]
        public static class SwitchPatch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(Junction __instance, byte branch) => RouteManager.AllowSwitch(__instance, branch);
            public static void Postfix()
            {
                Sessions.AddTag("junctions");
            }
        }

        [HarmonyPatch(typeof(Junction), nameof(Junction.Switch), new Type[] { typeof(Junction.SwitchMode) })]
        public static class TogglePatch
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(Junction __instance) => RouteManager.AllowSwitch(__instance, (byte)(__instance.selectedBranch + 1));
        }
    }
}
