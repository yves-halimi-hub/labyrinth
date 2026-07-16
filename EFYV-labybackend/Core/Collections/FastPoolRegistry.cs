using System.Collections.Generic;
using System;

namespace EFYVBackend.Core.Collections
{
    public static class FastPoolRegistry<T> where T : class
    {
        // Static backend registry. Completely detaches the Pool from the Unity GameObject hierarchy logic.
        private static readonly Dictionary<int, FastPool<T>> poolDictionary = new Dictionary<int, FastPool<T>>();

        public static void RegisterPool(int key, int capacity, Func<T> factoryMethod)
        {
            if (!poolDictionary.ContainsKey(key))
            {
                poolDictionary.Add(key, new FastPool<T>(capacity, factoryMethod));
            }
        }

        public static T Rent(int key)
        {
            if (poolDictionary.TryGetValue(key, out var pool))
            {
                return pool.Rent();
            }
            return null;
        }

        public static void Return(int key, T instance)
        {
            if (poolDictionary.TryGetValue(key, out var pool))
            {
                pool.Return(instance);
            }
        }

        public static bool Prewarm(int key, int count)
        {
            if (!poolDictionary.TryGetValue(key, out var pool)) return false;
            pool.Prewarm(count);
            return true;
        }
        
        public static void Clear()
        {
            poolDictionary.Clear();
        }
    }
}
