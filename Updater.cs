using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public class Updater : MonoBehaviour
    {
        public void Start()
        {
            StartCoroutine(PublishEventsCoro());
            StartCoroutine(RunActionsCoro());
        }

        private static GameObject? rootObject;

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
                PlayerData.CheckTransform();
            }
        }

        private static readonly Queue<(Action, TaskCompletionSource<bool>)> queuedActions = new Queue<(Action, TaskCompletionSource<bool>)>();
        private IEnumerator RunActionsCoro()
        {
            while (true)
            {
                yield return null;
                while (queuedActions.Count > 0)
                {
                    var (action, tcs) = queuedActions.Dequeue();
                    action();
                    tcs.SetResult(true);
                }
            }
        }

        public static Task PostAction(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            queuedActions.Enqueue((action, tcs));
            return tcs.Task;
        }
    }
}