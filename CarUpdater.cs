using DV.HUD;
using DV.Utils;
using HarmonyLib;

namespace DvMod.RemoteDispatch
{
    public static class CarUpdater
    {
        private static readonly InteriorControlsManager.ControlType[] WATCHED_CONTROL_TYPES = {
            InteriorControlsManager.ControlType.IndBrake,
            InteriorControlsManager.ControlType.TrainBrake,
            InteriorControlsManager.ControlType.Throttle,
            InteriorControlsManager.ControlType.Reverser
        };

        public static void MarkCarAsDirty(TrainCar car)
        {
            Sessions.AddTag($"carguid-{car.CarGUID}");
        }

        [HarmonyPatch(typeof(InteriorControlsManager), nameof(InteriorControlsManager.Start))]
        public static class ControlsUpdatedPatch
        {
            public static void Postfix(InteriorControlsManager __instance)
            {
                foreach (InteriorControlsManager.ControlType controlType in WATCHED_CONTROL_TYPES)
                    __instance.controls[controlType].controlImplBase.ValueChanged += args => MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
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
