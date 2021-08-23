using System.Collections;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class Updater : MonoBehaviour
    {
        public void Start()
        {
            StartCoroutine(PublishEventsCoro());
        }

        private static GameObject? rootObject;
        private static bool jobsDirty = false;

        public static void Create()
        {
            if (rootObject == null)
            {
                rootObject = new GameObject();
                GameObject.DontDestroyOnLoad(rootObject);
                rootObject.AddComponent<Updater>();
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

        private IEnumerator PublishEventsCoro()
        {
            while (true)
            {
                yield return WaitFor.Seconds(0.1f);
                CarUpdater.PublishUpdatedCars();
                PlayerData.PublishPlayerData();
                if (jobsDirty)
                {
                    JobData.PublishJobUpdate();
                    jobsDirty = false;
                }
            }
        }

        public static void MarkJobsAsDirty()
        {
            jobsDirty = true;
        }
    }
}