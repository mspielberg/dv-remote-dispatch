using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace DvMod.RemoteDispatch
{
    public static class PlayerData
    {
        private static World.Position previousPosition;
        private static float previousRotation;

        public static void CheckTransform()
        {
            var transform = PlayerManager.PlayerTransform;
            if (transform == null)
                return;
            var position = new World.Position(transform.position - WorldMover.currentMove);
            var rotation = transform.eulerAngles.y;
            if (!(
                ApproximatelyEquals(previousPosition.x, position.x)
                && ApproximatelyEquals(previousPosition.z, position.z)
                && ApproximatelyEquals(previousRotation, rotation)))
            {
                Sessions.AddTag("player");
                previousPosition = position;
                previousRotation = rotation;
            }
        }

        private static bool ApproximatelyEquals(float f1, float f2)
        {
            var delta = f1 - f2;
            return delta > -1e-3 && delta < 1e-3;
        }

        private static string GetLocalSteamName()
        {
            try
            {
                // https://wiki.facepunch.com/steamworks/SteamClient
                return Steamworks.SteamClient.Name ?? "steam name unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        public static JObject GetPlayerData()
        {
            CheckTransform();
            var res = new JObject();

            res[GetLocalSteamName()] = new JObject(
                // Do not change this format, it gets patched by the MP mod to add all client players
                new JProperty("color", "aqua"),
                new JProperty("position", previousPosition.ToLatLon().ToJson()),
                new JProperty("rotation", Math.Round(previousRotation, 2))
            );
            return res;
        }

        public static string GetPlayerDataJson()
        {
            return JsonConvert.SerializeObject(GetPlayerData());
        }
    }
}
