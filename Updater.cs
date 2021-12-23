using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading.Tasks;
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
                while (taskQueue.TryDequeue(out var action))
                    action();
                PlayerData.CheckTransform();
            }
        }

        private static readonly ConcurrentQueue<Action> taskQueue = new ConcurrentQueue<Action>();

        public static Task<T> RunOnMainThread<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();
            taskQueue.Enqueue(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }
    }
}