using DV.Logic.Job;
using DV.RemoteControls;
using DV.Utils;
using System.Collections.Specialized;
using System.Linq;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class LocoControl
    {
        public static bool CanBeControlled(TrainCar trainCar)
        {
            return trainCar.GetComponent<RemoteControllerModule>() != null;
        }
        
        public static RemoteControllerModule? GetLocoController(string guid)
        {
            return SingletonBehaviour<IdGenerator>.Instance
                .GetTrainCarByCarGuid(guid)
                ?.GetComponent<RemoteControllerModule>();
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
                    controller.controlsOverrider.Brake?.Set(value01);
                    break;
                case "independentBrake":
                    controller.controlsOverrider.IndependentBrake?.Set(value01);
                    break;
                case "reverser":
                    controller.controlsOverrider.Reverser?.Set(value01);
                    break;
                case "throttle":
                    controller.controlsOverrider.Throttle?.Set(value01);
                    break;
                case "couple":
                    controller.RemoteControllerCouple();
                    break;
                case "uncouple":
                    controller.Uncouple((int)value);
                    break;
                case "horn":
                    controller.controlsOverrider.Horn?.Set(value01);
                    break;
                case "sander":
                    controller.controlsOverrider.Sander?.Set(value01);
                    break;
                default:
                    return false;
                }
            }
            return true;
        }
    }
}
