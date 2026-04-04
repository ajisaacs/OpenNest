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
