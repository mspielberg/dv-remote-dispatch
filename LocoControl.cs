using DV.Logic.Job;
using System.Collections.Specialized;
using System.Linq;
using DV.RemoteControls;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class LocoControl
    {
        public const float CouplerRange = 0.5f;

        public static RemoteControllerModule? GetLocoController(string id)
        {
            return SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar.Values
                .FirstOrDefault(c => c.ID == id)?
                .GetComponent<RemoteControllerModule>();
        }

        public static bool RunCommand(RemoteControllerModule controller, NameValueCollection queryString)
        {
            for (int i = 0; i < queryString.Count; i++)
            {
                if (!float.TryParse(queryString.Get(i), out var value))
                    return false;
                var key = queryString.GetKey(i);
                float value01 = Mathf.Clamp01(value);
                switch (key)
                {
                case "trainBrake":
                    controller.controlsOverrider.Brake.Set(value01);
                    break;
                case "independentBrake":
                    controller.controlsOverrider.IndependentBrake.Set(value01);
                    break;
                case "reverser":
                    controller.controlsOverrider.Reverser.Set(value01);
                    break;
                case "throttle":
                    controller.controlsOverrider.Throttle.Set(value01);
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
            CarUpdater.MarkCarAsDirty(controller.car);
            return true;
        }
    }
}
