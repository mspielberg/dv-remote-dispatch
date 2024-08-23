using DV.Logic.Job;
using DV.RemoteControls;
using DV.ThingTypes;
using DV.Utils;
using DvMod.RemoteDispatch.Patches.Game;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public class CarData
    {
        public readonly string guid;
        public readonly float length;
        public readonly World.LatLon latlon;
        public readonly float rotation;
        public readonly string? jobId;
        public readonly string? destinationYardId;
        public readonly TrainCarType carType;

        protected CarData(string guid, float length, World.LatLon latlon, float rotation, string? jobId, string? destinationYardId, TrainCarType carType)
        {
            this.guid = guid;
            this.latlon = latlon;
            this.rotation = rotation;
            this.length = length;
            this.jobId = jobId;
            this.destinationYardId = destinationYardId;
            this.carType = carType;
        }

        public static CarData From(TrainCar trainCar)
        {
            if (LocoControl.CanBeControlled(trainCar))
                return new ControllableLocoData(trainCar);

            return new CarData(
                trainCar.CarGUID,
                trainCar.InterCouplerDistance,
                latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
                rotation: trainCar.transform.eulerAngles.y,
                jobId: JobData.JobIdForCar(trainCar),
                destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
                carType: trainCar.carType);
        }

        public virtual JObject ToJson()
        {
            return new JObject(
                new JProperty("guid", guid),
                new JProperty("length", (int)length),
                new JProperty("position", latlon.ToJson()),
                new JProperty("rotation", Math.Round(rotation, 2))
            );
        }

        public static JObject GetAllCarDataJson()
        {
            return JObject.FromObject(
                GetAllCarData().ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson()));
        }

        public static JObject? GetCarGuidDataJson(string guid)
        {
            var (carId, carData) = Updater.RunOnMainThread(() =>
            {
                var car = SingletonBehaviour<IdGenerator>.Instance.GetTrainCarByCarGuid(guid);
                if (car == null)
                    return default;
                return (car.ID, From(car));
            }).Result;
            if (carId == default)
                return null;
            var obj = carData.ToJson();
            obj.Add("id", carId);
            return obj;
        }

        public static Dictionary<string, CarData> GetAllCarData()
        {
            return Updater.RunOnMainThread(() =>
            {
                return SingletonBehaviour<IdGenerator>.Instance
                    .logicCarToTrainCar
                    .Values
                    .ToDictionary(car => car.ID, car => From(car));
            }).Result;
        }

        public static Dictionary<string, JObject> GetTrainsetData(int id)
        {
            return Updater.RunOnMainThread(() =>
            {
                var trainset = Trainset.allSets.Find(set => set.id == id);
                if (trainset == null)
                    return new Dictionary<string, JObject>();
                return trainset.cars.ToDictionary(car => car.ID, car => From(car).ToJson());
            }).Result;
        }

        public static JObject GetTrainsetDataJson(int id)
        {
            return JObject.FromObject(GetTrainsetData(id));
        }
    }

    public class ControllableLocoData : CarData
    {
        public readonly bool canCouple;
        public readonly bool isSlipping;
        public readonly int carsInFront;
        public readonly int carsInRear;
        public readonly float brakePipe;
        public readonly float forwardSpeed;
        public readonly float independentBrake;
        public readonly float reverser;
        public readonly float throttle;
        public readonly float trainBrake;

        public ControllableLocoData(TrainCar trainCar)
        : base(
            trainCar.CarGUID,
            trainCar.InterCouplerDistance,
            latlon: new World.Position(trainCar.transform.TransformPoint(trainCar.Bounds.center) - WorldMover.currentMove).ToLatLon(),
            rotation: trainCar.transform.eulerAngles.y,
            jobId: JobData.JobIdForCar(trainCar),
            destinationYardId: JobData.JobForCar(trainCar)?.chainData?.chainDestinationYardId,
            carType: trainCar.carType)
        {
            ILocomotiveRemoteControl controller = trainCar.GetComponent<ILocomotiveRemoteControl>();
            canCouple = controller.IsCouplerInRange(ExternalCouplingHandler.COUPLING_RANGE);
            isSlipping = controller.IsWheelslipping();
            carsInFront = controller.GetNumberOfCarsInFront();
            carsInRear = controller.GetNumberOfCarsInRear();
            forwardSpeed = trainCar.GetForwardSpeed();
            independentBrake = controller.GetTargetIndependentBrake();
            trainBrake = controller.GetTargetBrake();
            reverser = controller.GetReverserValue();
            throttle = controller.GetTargetThrottle();
            brakePipe = trainCar.brakeSystem.brakePipePressure;
        }

        override public JObject ToJson()
        {
            var carObj = base.ToJson();
            carObj.Add("canBeControlled", true);
            carObj.Add("canCouple", canCouple);
            carObj.Add("isSlipping", isSlipping);
            carObj.Add("carsInFront", carsInFront);
            carObj.Add("carsInRear", carsInRear);
            carObj.Add("forwardSpeed", forwardSpeed * 60 * 60 / 1000);
            carObj.Add("reverser", reverser);
            carObj.Add("independentBrake", independentBrake);
            carObj.Add("trainBrake", trainBrake);
            carObj.Add("throttle", throttle);
            carObj.Add("brakePipe", brakePipe);
            return carObj;
        }
    }
}
