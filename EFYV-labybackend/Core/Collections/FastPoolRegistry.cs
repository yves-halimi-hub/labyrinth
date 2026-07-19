using System.Collections.Generic;
using System;

namespace EFYVBackend.Core.Collections
{
    public static class FastPoolRegistry<T> where T : class
    {
        // Static backend registry. Completely detaches the Pool from the Unity GameObject hierarchy logic.
        private static readonly Dictionary<int, FastPool<T>> poolDictionary = new Dictionary<int, FastPool<T>>();

        // THREAD-SAFETY CONTRACT: the registry map itself is guarded by this lock, so
        // registration/lookup/clear from different threads can no longer corrupt the
        // dictionary. The pools handed out are NOT thread-safe - Rent/Return/Prewarm on
        // one pool must still happen on a single thread (the game loop) at a time.
        private static readonly object registryLock = new object();

        public static void RegisterPool(int key, int capacity, Func<T> factoryMethod)
        {
            // First registration wins, exactly as before; the FastPool constructor never
            // invokes the caller's factory, so constructing it under the lock is safe.
            lock (registryLock)
            {
                if (!poolDictionary.ContainsKey(key))
                {
                    poolDictionary.Add(key, new FastPool<T>(capacity, factoryMethod));
                }
            }
        }

        public static T Rent(int key)
        {
            FastPool<T> pool;
            lock (registryLock)
            {
                if (!poolDictionary.TryGetValue(key, out pool)) return null;
            }
            return pool.Rent();
        }

        public static void Return(int key, T instance)
        {
            FastPool<T> pool;
            lock (registryLock)
            {
                if (!poolDictionary.TryGetValue(key, out pool)) return;
            }
            pool.Return(instance);
        }

        public static bool Prewarm(int key, int count)
        {
            FastPool<T> pool;
            lock (registryLock)
            {
                if (!poolDictionary.TryGetValue(key, out pool)) return false;
            }
            pool.Prewarm(count);
            return true;
        }

        public static void Clear()
        {
            lock (registryLock)
            {
                poolDictionary.Clear();
            }
        }
    }
}
