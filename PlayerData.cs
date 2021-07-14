using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DvMod.RemoteDispatch
{
    public static class PlayerData
    {
        private static string? previousReport;

        public static string? GetPlayerDataJson()
        {
            var transform = PlayerManager.PlayerTransform;
            if (transform == null)
                return null;

            var position = new World.Position(transform.position - WorldMover.currentMove);
            return JsonConvert.SerializeObject(new JObject(
                new JProperty("type", "playerUpdate"),
                new JProperty("position", position.ToLatLon().ToJson()),
                new JProperty("rotation", transform.eulerAngles.y)
            ));
        }

        public static void PublishPlayerData()
        {
            var newData = GetPlayerDataJson();
            if (newData != null && newData != previousReport)
            {
                EventSource.PublishMessage(newData);
                previousReport = newData;
            }
        }
    }
}