using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using BackendConfig = EFYVBackend.Core.Data.EFYVLabyrinthConfig.Backend;

namespace EFYVBackend.Core.Collections
{
    // Interface required for elements to be tracked by the FastSwapList
    public interface IFastListTrackable
    {
        int ActiveListIndex { get; set; }
    }

    // A high-performance wrapper around System.Collections.Generic.List
    // It enforces O(1) "Swap-and-Pop" removals to prevent memory shifting overhead,
    // which is critical for thousands of rapidly dying entities (projectiles, enemies, particles).
    public class FastSwapList<T> where T : class, IFastListTrackable
    {
        private readonly List<T> items;
        private readonly ReadOnlyCollection<T> readOnlyItems;
        public IReadOnlyList<T> Items => readOnlyItems;

        public FastSwapList(int initialCapacity = BackendConfig.Collections.DefaultSwapListCapacity)
        {
            if (initialCapacity < BackendConfig.Collections.EmptyPoolCount) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            items = new List<T>(initialCapacity);
            readOnlyItems = items.AsReadOnly();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.ActiveListIndex != BackendConfig.Collections.UnregisteredListIndex) throw new InvalidOperationException();
            item.ActiveListIndex = items.Count;
            items.Add(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            int index = item.ActiveListIndex;
            int count = items.Count;
            
            // Validate the item is actually in this list at the expected spot
            if (index >= 0 && index < count && ReferenceEquals(items[index], item))
            {
                int lastIndex = count - 1;
                T lastItem = items[lastIndex];
                
                // Move the last item into the vacant slot
                items[index] = lastItem;
                lastItem.ActiveListIndex = index;
                
                // Pop the last element (O(1) operation in C# List)
                items.RemoveAt(lastIndex);
                
                // Mark the removed item as unregistered
                item.ActiveListIndex = BackendConfig.Collections.UnregisteredListIndex;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                items[i].ActiveListIndex = BackendConfig.Collections.UnregisteredListIndex;
            }
            items.Clear();
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => items.Count;
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => items[index];
        }
    }
}
