using HarmonyLib;

namespace DvMod.RemoteDispatch
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