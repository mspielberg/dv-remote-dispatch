using DV.Logic.Job;
using HarmonyLib;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using UnityEngine;

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

        public static bool RunCommand(LocoControllerBase controller, NameValueCollection queryString)
        {
            for (int i = 0; i < queryString.Count; i++)
            {
                if (!float.TryParse(queryString.Get(i), out var value))
                    return false;
                var key = queryString.GetKey(i);
                switch (key)
                {
                case "trainBrake":
                    controller.SetBrake(Mathf.Clamp01(value));
                    break;
                case "independentBrake":
                    controller.SetIndependentBrake(Mathf.Clamp01(value));
                    break;
                case "reverser":
                    controller.SetReverser(Mathf.Clamp(value, -1, 1));
                    break;
                case "throttle":
                    controller.SetThrottle(Mathf.Clamp01(value));
                    break;
                case "couple":
                    controller.RemoteControllerCouple();
                    break;
                case "uncouple":
                    controller.Uncouple((int)value);
                    break;
                default:
                    return false;
                }
            }
            return true;
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
                    {
                        CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                    }
                }
            }
            [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.SetIndependentBrake))]
            public static class SetIndependentBrakePatch
            {
                public static void Prefix(LocoControllerBase __instance, float nextTargetIndependentBrake)
                {
                    if (__instance is LocoControllerShunter
                        && !ApproximatelyEqual(__instance.independentBrake, nextTargetIndependentBrake))
                    {
                        CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                    }
                }
            }
            [HarmonyPatch(typeof(LocoControllerBase), nameof(LocoControllerBase.SetReverser))]
            public static class UpdatePatch
            {
                public static void Prefix(LocoControllerBase __instance, float position)
                {
                    if (__instance is LocoControllerShunter
                        && !ApproximatelyEqual(__instance.reverser, position))
                    {
                        CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                    }
                }
            }
            [HarmonyPatch(typeof(LocoControllerShunter), nameof(LocoControllerShunter.SetThrottle))]
            public static class ThrottleUpdatePatch
            {
                public static void Prefix(LocoControllerShunter __instance, float throttleLever)
                {
                    if (!ApproximatelyEqual(__instance.throttle, throttleLever))
                    {
                        CarUpdater.MarkCarAsDirty(TrainCar.Resolve(__instance.gameObject));
                    }
                }
            }
        }
    }
}
