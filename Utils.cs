using System.Collections.Generic;

namespace DvMod.RemoteDispatch
{
    public static class UtilExtensions
    {
        public static void Deconstruct<K,V>(this KeyValuePair<K,V> kvp, out K key, out V value)
        {
            key = kvp.Key;
            value = kvp.Value;
        }
    }
}