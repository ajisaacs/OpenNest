using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Collections
{
    public class ObservableList<T> : IList<T>, ICollection<T>, IEnumerable<T>
    {
        private readonly List<T> items;

        public event EventHandler<ItemAddedEventArgs<T>> ItemAdded;
        public event EventHandler<ItemRemovedEventArgs<T>> ItemRemoved;
        public event EventHandler<ItemChangedEventArgs<T>> ItemChanged;

        /// <summary>Raised once after <see cref="Reorder"/> (or a cutting commit) installs a new order.</summary>
        public event EventHandler ItemsReordered;

        public ObservableList()
        {
            items = new List<T>();
        }

        public void Add(T item)
        {
            var index = items.Count;
            items.Add(item);
            ItemAdded?.Invoke(this, new ItemAddedEventArgs<T>(item, index));
        }

        public void AddRange(IEnumerable<T> collection)
        {
            var index = items.Count;
            items.AddRange(collection);

            if (ItemAdded != null)
            {
                foreach (var item in collection)
                    ItemAdded.Invoke(this, new ItemAddedEventArgs<T>(item, index++));
            }
        }

        public void Insert(int index, T item)
        {
            items.Insert(index, item);
            ItemAdded?.Invoke(this, new ItemAddedEventArgs<T>(item, index));
        }

        public bool Remove(T item)
        {
            var success = items.Remove(item);
            if (success)
                ItemRemoved?.Invoke(this, new ItemRemovedEventArgs<T>(item, success));
            return success;
        }

        public void RemoveAt(int index)
        {
            var item = items[index];
            items.RemoveAt(index);
            ItemRemoved?.Invoke(this, new ItemRemovedEventArgs<T>(item, true));
        }

        public void Clear()
        {
            for (int i = items.Count - 1; i >= 0; --i)
                RemoveAt(i);
        }

        /// <summary>
        /// Changes only the order of the current items. <paramref name="order"/> must hold exactly
        /// the current non-null items: the same references with the same multiplicity. No
        /// ItemAdded/ItemRemoved is raised, so quantity accounting is untouched; ItemsReordered is
        /// raised once after the new order is installed. Invalid input throws before any change.
        /// </summary>
        public void Reorder(IEnumerable<T> order)
        {
            SetOrder(ValidateReorder(order));
            ItemsReordered?.Invoke(this, EventArgs.Empty);
        }

        internal T[] ValidateReorder(IEnumerable<T> order)
        {
            ArgumentNullException.ThrowIfNull(order);
            var proposed = order.ToArray();
            if (proposed.Length != items.Count)
                throw new ArgumentException("A reorder must contain exactly the current items.", nameof(order));
            var comparer = typeof(T).IsValueType
                ? EqualityComparer<T>.Default
                : (IEqualityComparer<T>)(object)ReferenceEqualityComparer.Instance;
            var counts = new Dictionary<T, int>(comparer);
            foreach (var item in items)
            {
                if (item == null)
                    throw new InvalidOperationException("A list holding null items cannot be reordered.");
                counts[item] = counts.TryGetValue(item, out var count) ? count + 1 : 1;
            }
            foreach (var item in proposed)
            {
                if (item == null || !counts.TryGetValue(item, out var count) || count == 0)
                    throw new ArgumentException("A reorder must contain exactly the current items.", nameof(order));
                counts[item] = count - 1;
            }
            return proposed;
        }

        // Validated order only; raises nothing so a commit can publish after a whole scope installs.
        internal void SetOrder(T[] order)
        {
            items.Clear();
            items.AddRange(order);
        }

        // Invokes every observer even if one throws, so a refresh failure cannot starve the rest.
        internal void RaiseItemsReordered(ICollection<Exception> errors)
        {
            if (ItemsReordered == null)
                return;
            foreach (var handler in ItemsReordered.GetInvocationList())
            {
                try
                {
                    ((EventHandler)handler)(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        }

        public int IndexOf(T item)
        {
            return items.IndexOf(item);
        }

        public T this[int index]
        {
            get => items[index];
            set
            {
                var old = items[index];
                items[index] = value;
                ItemChanged?.Invoke(this, new ItemChangedEventArgs<T>(old, value, index));
            }
        }

        public bool Contains(T item)
        {
            return items.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            items.CopyTo(array, arrayIndex);
        }

        public int Count => items.Count;

        public bool IsReadOnly => false;

        public IEnumerator<T> GetEnumerator()
        {
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return items.GetEnumerator();
        }
    }

    public class ItemAddedEventArgs<T> : EventArgs
    {
        public T Item { get; }
        public int Index { get; }

        public ItemAddedEventArgs(T item, int index)
        {
            Item = item;
            Index = index;
        }
    }

    public class ItemRemovedEventArgs<T> : EventArgs
    {
        public T Item { get; }
        public bool Succeeded { get; }

        public ItemRemovedEventArgs(T item, bool succeeded)
        {
            Item = item;
            Succeeded = succeeded;
        }
    }

    public class ItemChangedEventArgs<T> : EventArgs
    {
        public T OldItem { get; }
        public T NewItem { get; }
        public int Index { get; }

        public ItemChangedEventArgs(T oldItem, T newItem, int index)
        {
            OldItem = oldItem;
            NewItem = newItem;
            Index = index;
        }
    }

    public static class PlateCollectionExtensions
    {
        public static void RemoveEmptyPlates(this ObservableList<Plate> plates)
        {
            if (plates.Count < 2)
                return;

            for (int i = plates.Count - 1; i >= 0; --i)
            {
                if (plates[i].Parts.Count == 0)
                    plates.RemoveAt(i);
            }
        }

        public static int TotalCount(this ObservableList<Plate> plates)
        {
            return plates.Sum(plate => plate.Quantity);
        }
    }
}
