using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class CarUpdater
    {
        public static void MarkTrainsetAsDirty(Trainset trainset)
        {
            Sessions.AddTag($"trainset-{trainset.id}");
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
            CarSpawner.CarSpawned += OnCarsChanged;
            CarSpawner.CarAboutToBeDeleted += OnCarsChanged;
        }

        private static void OnCarsChanged(TrainCar _)
        {
            Sessions.AddTag("cars");
        }
    }
}