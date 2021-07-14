using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class CarUpdater : MonoBehaviour
    {
        private static readonly Dictionary<string, CarData> updatedCarData =
            new Dictionary<string, CarData>();

        public static void MarkCarAsDirty(TrainCar car)
        {
            updatedCarData[car.ID] = new CarData(car);
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
                MarkCarAsDirty(__instance);
            }
        }

        private static GameObject? rootObject;

        public static void Create()
        {
            if (rootObject == null)
            {
                rootObject = new GameObject();
                GameObject.DontDestroyOnLoad(rootObject);
                rootObject.AddComponent<CarUpdater>();
            }
        }

        public static void Destroy()
        {
            if (rootObject != null)
            {
                GameObject.Destroy(rootObject);
                rootObject = null;
            }
        }

        public void Start()
        {
            CarSpawner.CarSpawned += OnCarSpawned;
            CarSpawner.CarAboutToBeDeleted += OnCarAboutToBeDeleted;
            StartCoroutine(PublishEventsCoro());
        }

        private void OnCarSpawned(TrainCar car)
        {
            var jsonMessage = new JObject(
                new JProperty("type", "carSpawned"),
                new JProperty("carId", car.ID),
                new JProperty("carData", new CarData(car).ToJson()));
            EventSource.PublishMessage(JsonConvert.SerializeObject(jsonMessage));
        }

        private void OnCarAboutToBeDeleted(TrainCar car)
        {
            var jsonMessage = new JObject(
                new JProperty("type", "carDeleted"),
                new JProperty("carId", car.ID));
            EventSource.PublishMessage(JsonConvert.SerializeObject(jsonMessage));
        }

        private IEnumerator PublishEventsCoro()
        {
            while (true)
            {
                yield return WaitFor.Seconds(0.2f);
                if (updatedCarData.Count > 0)
                {
                    var jsonMessage = new JObject(
                        new JProperty("type", "carsUpdate"),
                        new JProperty("cars", JObject.FromObject(
                            updatedCarData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToJson())))
                    );
                    EventSource.PublishMessage(JsonConvert.SerializeObject(jsonMessage));
                    updatedCarData.Clear();
                }
            }
        }
    }
}