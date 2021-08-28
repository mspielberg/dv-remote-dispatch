using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DvMod.RemoteDispatch
{
    public class AsyncQueue<T>
    {
        private readonly object queueLock = new object();
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
        private readonly Queue<T> queue = new Queue<T>();

        public void Add(T item)
        {
            lock (queueLock)
            {
                queue.Enqueue(item);
            }
            semaphore.Release();
        }

        public bool Contains(T item)
        {
            lock (queueLock)
            {
                return queue.Contains(item);
            }
        }

        public T Take(TimeSpan timeSpan)
        {
            semaphore.Wait(timeSpan);
            lock (queueLock)
            {
                return queue.Dequeue();
            }
        }

        public bool TryTake(out T value)
        {
            var success = semaphore.Wait(0);
            if (success)
                lock (queueLock) value = queue.Dequeue();
            else
                value = default;
            return success;
        }

        public IEnumerable<T> TakeAll()
        {
            var result = new List<T>();
            while (semaphore.Wait(0))
            {
                lock (queueLock)
                {
                    result.Add(queue.Dequeue());
                }
            }
            return result;
        }

        public async Task<(bool, T)> TryTakeAsync(TimeSpan timeSpan)
        {
            var success = await semaphore.WaitAsync(timeSpan).ConfigureAwait(true);
            T value = default;
            if (success)
                lock (queueLock) value = queue.Dequeue();
            return (success, value);
        }
    }
}