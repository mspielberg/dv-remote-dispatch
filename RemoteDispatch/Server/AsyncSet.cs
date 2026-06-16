using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DvMod.RemoteDispatch
{
    public class AsyncSet<T>
    {
        private readonly object queueLock = new object();
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
        private readonly Queue<T> queue = new Queue<T>();

        public void Add(T item)
        {
            lock (queueLock)
            {
                if (!queue.Contains(item))
                {
                    queue.Enqueue(item);
                    semaphore.Release();
                }
            }
        }

        public IEnumerable<T> TakeAll()
        {
            lock (queueLock)
            {
                var result = new List<T>();
                while (semaphore.Wait(0))
                    result.Add(queue.Dequeue());
                return result;
            }
        }

        public async Task<(bool, T)> TryTakeAsync(TimeSpan timeSpan)
        {
            var success = await semaphore.WaitAsync(timeSpan).ConfigureAwait(true);
            T value = default;
            if (success)
                lock (queueLock) value = queue.Dequeue();
            return (success, value!);
        }
    }
}