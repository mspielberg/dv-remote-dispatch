using DV.RemoteControls;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class CarUpdater
    {
        public static void MarkCarAsDirty(TrainCar car)
        {
            Sessions.AddTag($"carguid-{car.CarGUID}");
        }

        [HarmonyPatch(typeof(RemoteControllerModule), nameof(RemoteControllerModule.Init))]
        public static class RemoteControllerModuleInitPatch
        {
            public static void Postfix(RemoteControllerModule __instance)
            {
                void ControlChanged(float _newValue)
                {
                    MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                }

                var overrider = __instance.controlsOverrider;
                overrider.Brake.ControlUpdated += ControlChanged;
                overrider.IndependentBrake.ControlUpdated += ControlChanged;
                overrider.Reverser.ControlUpdated += ControlChanged;
                overrider.Throttle.ControlUpdated += ControlChanged;
            }
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
            CarSpawner carSpawner = SingletonBehaviour<CarSpawner>.Instance;
            if (carSpawner == null)
            {
                Main.DebugLog(() => $"Tried to start {nameof(CarUpdater)} before {nameof(CarSpawner)} was initialized!");
                return;
            }

            carSpawner.CarSpawned += OnCarsChanged;
            carSpawner.CarAboutToBeDeleted += OnCarsChanged;
        }

        public static void Stop()
        {
            CarSpawner carSpawner = SingletonBehaviour<CarSpawner>.Instance;
            if (carSpawner == null)
                return;
            carSpawner.CarSpawned -= OnCarsChanged;
            carSpawner.CarAboutToBeDeleted -= OnCarsChanged;
        }

        private static void OnCarsChanged(TrainCar trainCar)
        {
            Sessions.AddTag("cars");
        }
    }
}
