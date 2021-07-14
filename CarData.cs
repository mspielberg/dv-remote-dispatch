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
        public readonly string? jobId;

        public CarData(bool isLoco, float length, World.LatLon latlon, float rotation, string? jobId)
        {
            this.isLoco = isLoco;
            this.latlon = latlon;
            this.rotation = rotation;
            this.length = length;
            this.jobId = jobId;
        }

        public CarData(TrainCar trainCar)
        : this(
            CarTypes.IsAnyLocomotiveOrTender(trainCar.carType),
            trainCar.logicCar.length,
            latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
            rotation: trainCar.transform.eulerAngles.y,
            jobId: JobData.JobIdForCar(trainCar))
        {
        }

        public JObject ToJson()
        {
            return new JObject(
                new JProperty("isLoco", isLoco),
                new JProperty("length", length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", rotation),
                new JProperty("jobId", jobId)
            );
        }

        public static string GetAllCarDataJson()
        {
            return JsonConvert.SerializeObject(GetAllCarData().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson()));
        }

        private static Dictionary<string, CarData> GetAllCarData()
        {
            return SingletonBehaviour<IdGenerator>.Instance
                .logicCarToTrainCar
                .ToDictionary(kvp => kvp.Key.ID, kvp => new CarData(kvp.Value));
        }
    }
}