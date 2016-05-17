using System;
using System.Collections;
using System.Collections.Generic;

namespace OpenNest.Collections
{
    public class PartCollection : IList<Part>, ICollection<Part>, IEnumerable<Part>
    {
        private readonly List<Part> parts;

        public event EventHandler<PartAddedEventArgs> PartAdded;

        public event EventHandler<PartRemovedEventArgs> PartRemoved;

        public event EventHandler<PartChangedEventArgs> PartChanged;

        public PartCollection()
        {
            parts = new List<Part>();
        }

        public void Add(Part item)
        {
            var index = parts.Count;
            parts.Add(item);

            if (PartAdded != null)
                PartAdded.Invoke(this, new PartAddedEventArgs(item, index));
        }

        public void AddRange(IEnumerable<Part> collection)
        {
            var index = parts.Count;
            parts.AddRange(collection);

            if (PartAdded != null)
            {
                foreach (var part in collection)
                    PartAdded.Invoke(this, new PartAddedEventArgs(part, index++));
            }
        }

        public void Insert(int index, Part item)
        {
            parts.Insert(index, item);

            if (PartAdded != null)
                PartAdded.Invoke(this, new PartAddedEventArgs(item, index));
        }

        public bool Remove(Part item)
        {
            var success = parts.Remove(item);

            if (PartRemoved != null)
                PartRemoved.Invoke(this, new PartRemovedEventArgs(item, success));

            return success;
        }

        public void RemoveAt(int index)
        {
            if (PartRemoved != null)
            {
                var part = parts[index];

                parts.RemoveAt(index);
                PartRemoved.Invoke(this, new PartRemovedEventArgs(part, true));
            }
            else
            {
                parts.RemoveAt(index);
            }
        }

        public void Clear()
        {
            for (int i = parts.Count - 1; i >= 0; --i)
                RemoveAt(i);
        }

        public int IndexOf(Part item)
        {
            return parts.IndexOf(item);
        }

        public Part this[int index]
        {
            get
            {
                return parts[index];
            }
            set
            {
                var old = parts[index];

                parts[index] = value;

                if (PartChanged != null)
                {
                    var eventArgs = new PartChangedEventArgs(old, value, index);
                    PartChanged.Invoke(this, eventArgs);
                }
            }
        }

        public bool Contains(Part item)
        {
            return parts.Contains(item);
        }

        public void CopyTo(Part[] array, int arrayIndex)
        {
            parts.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return parts.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public object ForEach { get; set; }

        public IEnumerator<Part> GetEnumerator()
        {
            return parts.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return parts.GetEnumerator();
        }
    }

    public class PartAddedEventArgs : EventArgs
    {
        public readonly Part Part;
        public readonly int Index;

        public PartAddedEventArgs(Part part, int index)
        {
            Part = part;
            Index = index;
        }
    }

    public class PartRemovedEventArgs : EventArgs
    {
        public readonly Part Part;
        public readonly bool Succeeded;

        public PartRemovedEventArgs(Part part, bool succeeded)
        {
            Part = part;
            Succeeded = succeeded;
        }
    }

    public class PartChangedEventArgs : EventArgs
    {
        public readonly Part OldPart;
        public readonly Part NewPart;
        public readonly int Index;

        public PartChangedEventArgs(Part oldPart, Part newPart, int index)
        {
            OldPart = oldPart;
            NewPart = newPart;
            Index = index;
        }
    }
}
