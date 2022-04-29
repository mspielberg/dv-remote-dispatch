using DV.Logic.Job;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DvMod.RemoteDispatch
{
    public static class LocoControl
    {
        public const float CouplerRange = 0.5f;

        public static bool CanBeControlled(TrainCarType carType)
        {
            return carType == TrainCarType.LocoShunter;
        }

        public static LocoControllerBase? GetLocoController(string id)
        {
            return SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar.Values
                .FirstOrDefault(c => c.ID == id && CanBeControlled(c.carType))
                ?.GetComponent<LocoControllerBase>();
        }

        public static void SetCoast(LocoControllerBase controller)
        {
            controller.SetThrottle(0f);
            controller.SetBrake(0f);
            controller.SetIndependentBrake(0f);
        }

        public static void SetForward(LocoControllerBase controller)
        {
            controller.SetThrottle(0f);
            controller.SetBrake(0f);
            controller.SetIndependentBrake(0f);
            controller.SetReverser(1f);
            controller.SetThrottle(0.4f);
        }

        public static void SetReverse(LocoControllerBase controller)
        {
            controller.SetThrottle(0f);
            controller.SetBrake(0f);
            controller.SetIndependentBrake(0f);
            controller.SetReverser(-1f);
            controller.SetThrottle(0.4f);
        }

        public static void SetStop(LocoControllerBase controller)
        {
            controller.SetThrottle(0f);
            controller.SetBrake(1f);
            controller.SetIndependentBrake(1f);
            controller.SetReverser(0f);
        }

        public static bool IsSupportedCommand(string command)
        {
            return command == "coast" || command == "forward" || command == "reverse" || command == "stop";
        }

        public static void RunCommand(LocoControllerBase controller, string command)
        {
            switch (command)
            {
                case "coast":
                    SetCoast(controller);
                    break;
                case "forward":
                    SetForward(controller);
                    break;
                case "reverse":
                    SetReverse(controller);
                    break;
                case "stop":
                    SetStop(controller);
                    break;
            }
        }

        public static class ControllerUpdatePatches
        {
            private static bool ApproximatelyEqual(float a, float b, float epsilon = 0.01f)
            {
                if (a == b)
                    return true;
                float diff = a - b;
                return diff >= -epsilon && diff <= epsilon;
            }

            [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.SetBrake))]
            public static class SetBrakePatch
            {
                public static void Prefix(LocoControllerBase __instance, float nextTargetBrake)
                {
                    if (__instance is LocoControllerShunter
                        && !ApproximatelyEqual(__instance.brake, nextTargetBrake))
                       CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                }
            }
            [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.SetIndependentBrake))]
            public static class SetIndependentBrakePatch
            {
                public static void Prefix(LocoControllerBase __instance, float nextTargetIndependentBrake)
                {
                    if (__instance is LocoControllerShunter
                        && !ApproximatelyEqual(__instance.independentBrake, nextTargetIndependentBrake))
                       CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                }
            }
            [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.SetReverser))]
            public static class UpdatePatch
            {
                public static void Prefix(LocoControllerBase __instance, float position)
                {
                    if (__instance is LocoControllerShunter
                        && !ApproximatelyEqual(__instance.reverser, position))
                       CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                }
            }
            [HarmonyPatch(typeof(LocoControllerShunter), nameof(LocoControllerShunter.SetThrottle))]
            public static class ThrottleUpdatePatch
            {
                public static void Prefix(LocoControllerShunter __instance, float throttleLever)
                {
                    if (!ApproximatelyEqual(__instance.throttle, throttleLever))
                       CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                }
            }
        }
    }
}
