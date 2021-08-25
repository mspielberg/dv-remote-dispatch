using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class CarUpdater
    {
        private static readonly HashSet<int> dirtyTrainsetIds = new HashSet<int>();

        public static void MarkTrainsetAsDirty(Trainset trainset)
        {
            dirtyTrainsetIds.Add(trainset.id);
        }

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Update))]
        public static class UpdatePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                if (__instance.logicCar == null)
                    return;
                if (__instance.isStationary)
                    return;
                MarkTrainsetAsDirty(__instance.trainset);
            }
        }

        static CarUpdater()
        {
            CarSpawner.CarSpawned += OnCarSpawned;
            CarSpawner.CarAboutToBeDeleted += OnCarAboutToBeDeleted;
        }

        private static void OnCarSpawned(TrainCar car)
        {
            var jsonMessage = new JObject(
                new JProperty("type", "carSpawned"),
                new JProperty("carId", car.ID),
                new JProperty("carData", new CarData(car).ToJson()));
            EventSource.PublishMessage(JsonConvert.SerializeObject(jsonMessage));
        }

        private static void OnCarAboutToBeDeleted(TrainCar car)
        {
            var jsonMessage = new JObject(
                new JProperty("type", "carDeleted"),
                new JProperty("carId", car.ID));
            EventSource.PublishMessage(JsonConvert.SerializeObject(jsonMessage));
        }

        public static void PublishUpdatedCars()
        {
            if (dirtyTrainsetIds.Count > 0)
            {
                var jsonMessage = new JObject(
                    new JProperty("type", "trainsetsUpdate"),
                    new JProperty("trainsetIds", dirtyTrainsetIds.ToArray()));
                EventSource.PublishMessage(JsonConvert.SerializeObject(jsonMessage));
                dirtyTrainsetIds.Clear();
            }
        }
    }
}