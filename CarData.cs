using DV.Logic.Job;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public class CarData
    {
        public readonly float length;
        public readonly World.LatLon latlon;
        public readonly float rotation;
        public readonly string? jobId;
        public readonly string? destinationYardId;
        public readonly float speed;

        public CarData(float length, World.LatLon latlon, float rotation, string? jobId, string? destinationYardId, float speed)
        {
            this.latlon = latlon;
            this.rotation = rotation;
            this.length = length;
            this.jobId = jobId;
            this.destinationYardId = destinationYardId;
            this.speed = speed;
        }

        public CarData(TrainCar trainCar)
        : this(
            trainCar.InterCouplerDistance,
            latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
            rotation: trainCar.transform.eulerAngles.y,
            jobId: JobData.JobIdForCar(trainCar),
            destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
            speed: trainCar.GetForwardSpeed())
        {
        }

        public JObject ToJson()
        {
            return new JObject(
                new JProperty("length", (int)length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", Math.Round(rotation, 2)),
                new JProperty("speed", speed)
            );
        }

        public static string GetAllCarDataJson()
        {
            return JsonConvert.SerializeObject(
                GetAllCarData().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson()));
        }

        public static Dictionary<string, CarData> GetAllCarData()
        {
            return SingletonBehaviour<IdGenerator>.Instance
                .logicCarToTrainCar
                .ToDictionary(kvp => kvp.Key.ID, kvp => new CarData(kvp.Value));
        }

        public static Dictionary<string, JObject> GetTrainsetData(int id)
        {
            var trainset = Trainset.allSets.Find(set => set.id == id);
            if (trainset == null)
                return new Dictionary<string, JObject>();
            return trainset.cars.ToDictionary(car => car.ID, car => new CarData(car).ToJson());
        }

        public static string GetTrainsetDataJson(int id)
        {
            return JsonConvert.SerializeObject(GetTrainsetData(id));
        }
    }
}