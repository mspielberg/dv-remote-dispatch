using DV.Logic.Job;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class LocoControl
    {
        public static LocoControllerBase? GetLocoController(string id)
        {
            return SingletonBehaviour<IdGenerator>.Instance.logicCarToTrainCar.Values.FirstOrDefault(c => c.ID == id)?.GetComponent<LocoControllerBase>();
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
    }
}