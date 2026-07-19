using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Collections
{
    // Array-backed pool with bounded, lazy factory creation.
    //
    // RENT CONTRACT (explicit): a null result from Rent() means exactly one thing -
    // the pool is exhausted (all `Capacity` items are currently rented). A factory
    // that returns null (or a duplicate reference) is a programming error and throws
    // InvalidOperationException instead of being conflated with exhaustion, so callers
    // can rely on "null == try again later" without masking broken factories.
    public class FastPool<T> where T : class
    {
        private readonly T[] items;
        private readonly Dictionary<T, int> createdIndices;
        private readonly bool[] available;
        private readonly Func<T> factoryMethod;
        private int head;
        private int createdCount;

        public int Capacity => items.Length;
        public int CreatedCount => createdCount;
        public int AvailableCount => head;

        public FastPool(int capacity, Func<T> factoryMethod)
        {
            if (capacity < BackendConfig.Collections.EmptyPoolCount) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (factoryMethod == null) throw new ArgumentNullException(nameof(factoryMethod));

            items = new T[capacity];
            createdIndices = new Dictionary<T, int>(capacity, ReferenceIdentityComparer.Instance);
            available = new bool[capacity];
            this.factoryMethod = factoryMethod;
        }

        // Returns null ONLY when the pool is exhausted; see the class-level contract.
        public T Rent()
        {
            if (head > BackendConfig.Collections.EmptyPoolCount)
            {
                head--;
                T pooledItem = items[head];
                items[head] = null;
                available[createdIndices[pooledItem]] = false;
                return pooledItem;
            }

            if (createdCount == items.Length) return null;
            T item = factoryMethod();
            if (item == null) throw new InvalidOperationException();
            if (createdIndices.ContainsKey(item)) throw new InvalidOperationException();
            createdIndices.Add(item, createdCount);
            createdCount++;
            return item;
        }

        public void Return(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (!createdIndices.TryGetValue(item, out int itemIndex)) throw new ArgumentException(null, nameof(item));
            if (available[itemIndex]) throw new InvalidOperationException();
            if (head >= createdCount) throw new InvalidOperationException();

            items[head] = item;
            head++;
            available[itemIndex] = true;
        }

        public void Prewarm(int targetCreatedCount)
        {
            if (targetCreatedCount < BackendConfig.Collections.EmptyPoolCount || targetCreatedCount > items.Length)
                throw new ArgumentOutOfRangeException(nameof(targetCreatedCount));

            while (createdCount < targetCreatedCount)
            {
                T item = factoryMethod();
                if (item == null) throw new InvalidOperationException();
                if (createdIndices.ContainsKey(item)) throw new InvalidOperationException();
                items[head] = item;
                createdIndices.Add(item, createdCount);
                available[createdCount] = true;
                head++;
                createdCount++;
            }
        }

        private sealed class ReferenceIdentityComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
