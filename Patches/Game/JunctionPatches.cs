using HarmonyLib;

namespace DvMod.RemoteDispatch.Patches.Game
{
    public static class JunctionPatches
    {
        [HarmonyPatch(typeof(Junction), nameof(Junction.Switch))]
        public static class SwitchPatch
        {
            public static void Postfix()
            {
                Sessions.AddTag("junctions");
            }
        }
    }
}