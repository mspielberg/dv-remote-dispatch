using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class CarUpdater
    {
        public static void MarkCarAsDirty(TrainCar car)
        {
            Sessions.AddTag($"carguid-{car.CarGUID}");
        }

        public static void MarkTrainsetAsDirty(Trainset trainset)
        {
            Sessions.AddTag($"trainset-{trainset.id}");
        }

        [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.Update))]
        public static class UpdatePatch
        {
            public static void Postfix(TrainCar __instance)
            {
                if (__instance.logicCar == null || __instance.isStationary)
                    return;
                MarkTrainsetAsDirty(__instance.trainset);
            }
        }

        public static void Start()
        {
            CarSpawner.CarSpawned += OnCarsChanged;
            CarSpawner.CarAboutToBeDeleted += OnCarsChanged;
        }

        public static void Stop()
        {
            CarSpawner.CarSpawned -= OnCarsChanged;
            CarSpawner.CarAboutToBeDeleted -= OnCarsChanged;
        }

        private static void OnCarsChanged(TrainCar trainCar)
        {
            Sessions.AddTag("cars");
        }
    }
}
