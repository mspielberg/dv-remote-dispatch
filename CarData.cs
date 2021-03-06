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
        public readonly TrainCarType carType;
        public readonly float forwardSpeed;

        public CarData(float length, World.LatLon latlon, float rotation, string? jobId, string? destinationYardId, TrainCarType carType, float forwardSpeed)
        {
            this.latlon = latlon;
            this.rotation = rotation;
            this.length = length;
            this.jobId = jobId;
            this.destinationYardId = destinationYardId;
            this.carType = carType;
            this.forwardSpeed = forwardSpeed;
        }

        public CarData(TrainCar trainCar)
        : this(
            trainCar.InterCouplerDistance,
            latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
            rotation: trainCar.transform.eulerAngles.y,
            jobId: JobData.JobIdForCar(trainCar),
            destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
            carType: trainCar.carType,
            forwardSpeed: trainCar.GetForwardSpeed())
        {
        }

        public JObject ToJson()
        {
            var carObj = new JObject(
                new JProperty("length", (int)length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", Math.Round(rotation, 2))
            );
            if (LocoControl.CanBeControlled(carType))
            {
                carObj.Add("canBeControlled", true);
                carObj.Add("forwardSpeed", forwardSpeed * 60 * 60 / 1000);
            }
            return carObj;
        }

        public static string GetAllCarDataJson()
        {
            return JsonConvert.SerializeObject(
                GetAllCarData().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson()));
        }

        public static Dictionary<string, CarData> GetAllCarData()
        {
            return Updater.RunOnMainThread(() =>
            {
                return SingletonBehaviour<IdGenerator>.Instance
                    .logicCarToTrainCar
                    .ToDictionary(kvp => kvp.Key.ID, kvp => new CarData(kvp.Value));
            }).Result;
        }

        public static Dictionary<string, JObject> GetTrainsetData(int id)
        {
            return Updater.RunOnMainThread(() =>
            {
                var trainset = Trainset.allSets.Find(set => set.id == id);
                if (trainset == null)
                    return new Dictionary<string, JObject>();
                return trainset.cars.ToDictionary(car => car.ID, car => new CarData(car).ToJson());
            }).Result;
        }

        public static string GetTrainsetDataJson(int id)
        {
            return JsonConvert.SerializeObject(GetTrainsetData(id));
        }
    }
}