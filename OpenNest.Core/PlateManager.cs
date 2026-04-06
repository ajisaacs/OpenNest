using OpenNest.Collections;
using System;

namespace OpenNest
{
    public class PlateChangedEventArgs : EventArgs
    {
        public Plate Plate { get; }
        public int Index { get; }

        public PlateChangedEventArgs(Plate plate, int index)
        {
            Plate = plate;
            Index = index;
        }
    }

    public class PlateManager : IDisposable
    {
        private readonly Nest nest;
        private bool disposed;

        public event EventHandler<PlateChangedEventArgs> CurrentPlateChanged;
        public event EventHandler PlateListChanged;

        public PlateManager(Nest nest)
        {
            this.nest = nest;
            nest.Plates.ItemAdded += OnPlateAdded;
            nest.Plates.ItemRemoved += OnPlateRemoved;
        }

        public int CurrentIndex { get; private set; }

        public Plate CurrentPlate => nest.Plates.Count > 0 ? nest.Plates[CurrentIndex] : null;

        public int Count => nest.Plates.Count;

        public bool IsFirst => Count == 0 || CurrentIndex <= 0;

        public bool IsLast => CurrentIndex + 1 >= Count;

        public void LoadFirst()
        {
            if (Count == 0)
                return;

            CurrentIndex = 0;
            FireCurrentPlateChanged();
        }

        public void LoadLast()
        {
            if (Count == 0)
                return;

            CurrentIndex = Count - 1;
            FireCurrentPlateChanged();
        }

        public bool LoadNext()
        {
            if (CurrentIndex + 1 >= Count)
                return false;

            CurrentIndex++;
            FireCurrentPlateChanged();
            return true;
        }

        public bool LoadPrevious()
        {
            if (Count == 0 || CurrentIndex - 1 < 0)
                return false;

            CurrentIndex--;
            FireCurrentPlateChanged();
            return true;
        }

        public void LoadAt(int index)
        {
            if (index < 0 || index >= Count)
                return;

            CurrentIndex = index;
            FireCurrentPlateChanged();
        }

        private void OnPlateAdded(object sender, ItemAddedEventArgs<Plate> e)
        {
            PlateListChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPlateRemoved(object sender, ItemRemovedEventArgs<Plate> e)
        {
            if (CurrentIndex >= Count && Count > 0)
                CurrentIndex = Count - 1;

            PlateListChanged?.Invoke(this, EventArgs.Empty);
        }

        private void FireCurrentPlateChanged()
        {
            CurrentPlateChanged?.Invoke(this, new PlateChangedEventArgs(CurrentPlate, CurrentIndex));
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            nest.Plates.ItemAdded -= OnPlateAdded;
            nest.Plates.ItemRemoved -= OnPlateRemoved;
        }
    }
}
