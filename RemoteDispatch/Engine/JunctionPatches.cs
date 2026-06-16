using HarmonyLib;
using System;

namespace DvMod.RemoteDispatch
{
    public static class JunctionPatches
    {
        [HarmonyPatch(typeof(Junction), nameof(Junction.Switch), new Type[] { typeof(Junction.SwitchMode), typeof(byte) })]
        public static class SwitchPatch
        {
            public static void Postfix()
            {
                Sessions.AddTag("junctions");
            }
        }
    }
}