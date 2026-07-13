using DV.JObjectExtstensions;
using DV.LocoRestoration;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
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
                var car = TrainCarRegistry.Instance.GetTrainCarByCarGuid(guid);
                if (car == null || !ShouldReturnTrainCar(car))
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
                var carDataDict = TrainCarRegistry.Instance
                    .logicCarToTrainCar
                    .Values
                    .Where(ShouldReturnTrainCar)
                    .ToDictionary(car => car.ID, car => From(car));

                if (Main.PersistentJobsHooked) 
                    AddSuspendedCarsData(ref carDataDict);
                return carDataDict;
            }).Result;
        }

        private static void AddSuspendedCarsData(ref Dictionary<string, CarData> carDataDict)
        {
            try
            {
                var suspendedCarObjects = (Main.PersJobsSuspendedCarObjectsDict?.GetValue(null)) as Dictionary<string, JObject>;
                var trainCarTypeToInterCouplerDistance = (Main.PersJobsTrainCarTypeToInterCouplerDistanceDict?.GetValue(null)) as Dictionary<TrainCarType, float>;
                var SuspendedCarGUIDToJobChainController = (Main.PersJobsSuspendedCarGUIDToJobChainControllerDict?.GetValue(null)) as Dictionary<string, JobChainController>;

                if (suspendedCarObjects == null || trainCarTypeToInterCouplerDistance == null || SuspendedCarGUIDToJobChainController == null)
                {
                    Main.DebugLog(() => $"Failed to access PersJobs dictionaries, won´t show suspended cars!");
                    return;
                }

                foreach (var item in suspendedCarObjects.Values)
                {
                    TrainCarType tct = (TrainCarType)item.GetInt("type")!.Value;
                    var guid = item.GetString("carGuid");
                    var id = item.GetString("id");

                    if (!trainCarTypeToInterCouplerDistance.TryGetValue(tct, out float interCouplerDistance))
                    {
                        Main.DebugLog(() => $"Warning: TrainCarType {tct} has no saved length.");
                        interCouplerDistance = 15f;
                    }

                    Job? currentJobInChain = SuspendedCarGUIDToJobChainController.GetValueSafe(guid)?.currentJobInChain;

                    CarData cd = new CarData(
                        guid,
                        interCouplerDistance,
                        new World.Position(item.GetVector3("position")!.Value).ToLatLon(),
                        item.GetVector3("rotation")!.Value.y,
                        currentJobInChain?.ID,
                        currentJobInChain?.chainData.chainDestinationYardId,
                        tct);

                    carDataDict[id] = cd;
                }
            }
            catch (Exception ex)
            {
                Main.DebugLog(() => $"Failed to include suspended cars in update, exception: \n{ex}");
            }
        }

        public static Dictionary<string, JObject> GetTrainsetData(int id)
        {
            return Updater.RunOnMainThread(() =>
            {
                var trainset = Trainset.allSets.Find(set => set.id == id);
                if (trainset == null)
                    return new Dictionary<string, JObject>();
                return trainset.cars
                    .Where(ShouldReturnTrainCar)
                    .ToDictionary(car => car.ID, car => From(car).ToJson());
            }).Result;
        }

        public static JObject GetTrainsetDataJson(int id)
        {
            return JObject.FromObject(GetTrainsetData(id));
        }

        public static bool ShouldReturnTrainCar(TrainCar trainCar)
        {
            if (Main.settings.showUndiscoveredLocomotives)
                return true;
            var state = LocoRestorationController.GetForTrainCar(trainCar)?.State;
            return !state.HasValue
                || state.Value >= LocoRestorationController.RestorationState.S3_RerailedCars;
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
