using DV.Logic.Job;
using DV.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public class CarData
    {
        public readonly bool isLoco;
        public readonly float length;
        public readonly World.LatLon latlon;
        public readonly float rotation;

        public CarData(bool isLoco, float length, World.LatLon latlon, float rotation)
        {
            this.isLoco = isLoco;
            this.latlon = latlon;
            this.rotation = rotation;
            this.length = length;
        }

        public JObject ToJson()
        {
            return new JObject(
                new JProperty("isLoco", isLoco),
                new JProperty("length", length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", rotation)
            );
        }

        public static string GetAllCarDataJson()
        {
            return JsonConvert.SerializeObject(GetAllCarData().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson()));
        }

        private static Dictionary<string, CarData> GetAllCarData()
        {
            return SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar.ToDictionary(
                kvp => kvp.Key.ID,
                kvp =>
                {
                    var logicCar = kvp.Key;
                    var trainCar = kvp.Value;
                    return new CarData(
                        trainCar.IsLoco,
                        logicCar.length,
                        latlon: new World.Position(trainCar.transform.position - WorldMover.currentMove).ToLatLon(),
                        rotation: trainCar.transform.eulerAngles.y);
                });
        }
    }
}