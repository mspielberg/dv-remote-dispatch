using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace DvMod.RemoteDispatch
{
    public static class JunctionPatches
    {
        [HarmonyPatch(typeof(Junction), nameof(Junction.Switch))]
        public static class SwitchPatch
        {
            public static void Postfix(Junction __instance)
            {
                var junctionId = Array.IndexOf(JunctionsSaveManager.OrderedJunctions, __instance);
                var messageJson = new JObject(
                    new JProperty("type", "junctionSwitched"),
                    new JProperty("junctionId", junctionId),
                    new JProperty("selectedBranch", __instance.selectedBranch)
                );
                EventSource.PublishMessage(JsonConvert.SerializeObject(messageJson));
            }
        }
    }
}