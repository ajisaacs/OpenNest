using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Collections
{
    public class PlateCollection : IList<Plate>, ICollection<Plate>, IEnumerable<Plate>
    {
        private List<Plate> plates;

        public event EventHandler<PlateAddedEventArgs> PlateAdded;

        public event EventHandler<PlateRemovedEventArgs> PlateRemoved;

        public event EventHandler<PlateChangedEventArgs> PlateChanged;

        public PlateCollection()
        {
            plates = new List<Plate>();
        }

        public void Add(Plate item)
        {
            var index = plates.Count;
            plates.Add(item);

            if (PlateAdded != null)
                PlateAdded.Invoke(this, new PlateAddedEventArgs(item, index));
        }

        public void AddRange(IEnumerable<Plate> collection)
        {
            var index = plates.Count;
            plates.AddRange(collection);

            if (PlateAdded != null)
            {
                foreach (var plate in collection)
                    PlateAdded.Invoke(this, new PlateAddedEventArgs(plate, index++));
            }
        }

        public void Insert(int index, Plate item)
        {
            plates.Insert(index, item);

            if (PlateAdded != null)
                PlateAdded.Invoke(this, new PlateAddedEventArgs(item, index));
        }

        public bool Remove(Plate item)
        {
            var success = plates.Remove(item);

            if (PlateRemoved != null)
                PlateRemoved.Invoke(this, new PlateRemovedEventArgs(item, success));

            return success;
        }

        public void RemoveAt(int index)
        {
            var plate = plates[index];
            plates.RemoveAt(index);

            if (PlateRemoved != null)
                PlateRemoved.Invoke(this, new PlateRemovedEventArgs(plate, true));
        }

        public void Clear()
        {
            for (int i = plates.Count - 1; i >= 0; --i)
                RemoveAt(i);
        }

        public int IndexOf(Plate item)
        {
            return plates.IndexOf(item);
        }

        public Plate this[int index]
        {
            get
            {
                return plates[index];
            }
            set
            {
                var old = plates[index];

                plates[index] = value;

                if (PlateChanged != null)
                {
                    var eventArgs = new PlateChangedEventArgs(old, value, index);
                    PlateChanged.Invoke(this, eventArgs);
                }
            }
        }

        public bool Contains(Plate item)
        {
            return plates.Contains(item);
        }

        public void CopyTo(Plate[] array, int arrayIndex)
        {
            plates.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return plates.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public IEnumerator<Plate> GetEnumerator()
        {
            return plates.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return plates.GetEnumerator();
        }

        public void RemoveEmptyPlates()
        {
            if (Count < 2)
                return;

            for (int i = Count - 1; i >= 0; --i)
            {
                if (this[i].Parts.Count == 0)
                    RemoveAt(i);
            }
        }

        public int TotalCount
        {
            get { return plates.Sum(plate => plate.Quantity); }
        }
    }

    public class PlateAddedEventArgs : EventArgs
    {
        public readonly Plate Plate;
        public readonly int Index;

        public PlateAddedEventArgs(Plate plate, int index)
        {
            Plate = plate;
            Index = index;
        }
    }

    public class PlateChangedEventArgs : EventArgs
    {
        public readonly Plate OldPlate;
        public readonly Plate NewPlate;
        public readonly int Index;

        public PlateChangedEventArgs(Plate oldPlate, Plate newPlate, int index)
        {
            OldPlate = oldPlate;
            NewPlate = newPlate;
            Index = index;
        }
    }

    public class PlateRemovedEventArgs : EventArgs
    {
        public readonly Plate Plate;
        public readonly bool Succeeded;

        public PlateRemovedEventArgs(Plate plate, bool succeeded)
        {
            Plate = plate;
            Succeeded = succeeded;
        }
    }
}
