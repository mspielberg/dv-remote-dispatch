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
        public bool isLoco;
        public World.LatLon position;
        public float rotation;

        public CarData(bool isLoco, World.LatLon position, float rotation)
        {
            this.isLoco = isLoco;
            this.position = position;
            this.rotation = rotation;
        }

        public JObject ToJson()
        {
            return new JObject(
                new JProperty("isLoco", isLoco),
                new JProperty("position", position.ToJson()),
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
                    var trainCar = kvp.Value;
                    var position = trainCar.transform.position - WorldMover.currentMove;
                    var normalizedPosition = new World.Position(position);
                    var rotation = trainCar.transform.eulerAngles.y;
                    return new CarData(trainCar.IsLoco, normalizedPosition.ToLatLon(), rotation);
                });
        }
    }
}