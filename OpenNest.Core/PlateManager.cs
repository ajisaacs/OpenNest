using System;
using OpenNest.Collections;

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
        private bool suppressNavigation;
        private bool batching;
        private Plate subscribedLast;
        private Plate subscribedSecondToLast;

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

        public bool CanRemoveCurrent =>
            Count > 1 && CurrentPlate != null && CurrentPlate.Parts.Count > 0;

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

        public void EnsureSentinel()
        {
            suppressNavigation = true;
            try
            {
                if (Count == 0 || nest.Plates[^1].Parts.Count > 0)
                    nest.CreatePlate();

                while (
                    Count > 1
                    && nest.Plates[^1].Parts.Count == 0
                    && nest.Plates[^2].Parts.Count == 0
                )
                {
                    nest.Plates.RemoveAt(Count - 1);
                }
            }
            finally
            {
                suppressNavigation = false;
            }

            SubscribeToTailPlates();
        }

        /// <summary>
        /// Applies a nest-defaults edit, moving only an untouched trailing empty sheet
        /// that matched the old defaults to the new defaults.
        /// </summary>
        public void ChangePlateDefaults(Action changeDefaults)
        {
            ArgumentNullException.ThrowIfNull(changeDefaults);
            var defaults = nest.PlateDefaults;
            var trailing = Count > 0 ? nest.Plates[^1] : null;
            // Exact configuration equality, not geometric tolerance: even a small edit
            // makes this configured stock rather than the untouched workspace sentinel.
            var followsDefaults = trailing != null
                && trailing.Parts.Count == 0
                && trailing.CutOffs.Count == 0
                && trailing.Outline == null
                && trailing.Size.Width == defaults.Size.Width
                && trailing.Size.Length == defaults.Size.Length
                && trailing.Quadrant == defaults.Quadrant
                && trailing.EdgeSpacing.Left == defaults.EdgeSpacing.Left
                && trailing.EdgeSpacing.Bottom == defaults.EdgeSpacing.Bottom
                && trailing.EdgeSpacing.Right == defaults.EdgeSpacing.Right
                && trailing.EdgeSpacing.Top == defaults.EdgeSpacing.Top
                && trailing.PartSpacing == defaults.PartSpacing
                && trailing.RemovedClamps.Count == 0
                && Equals(trailing.ClampRail, defaults.ClampRail)
                && (defaults.ClampRail == null
                    ? trailing.ClampZones.Count == 0
                    : defaults.ClampRail.Matches(trailing));

            changeDefaults();

            if (!followsDefaults)
                return;

            defaults = nest.PlateDefaults;
            trailing.Size = defaults.Size;
            trailing.Quadrant = defaults.Quadrant;
            trailing.EdgeSpacing = defaults.EdgeSpacing;
            trailing.PartSpacing = defaults.PartSpacing;
            trailing.SetClampRail(defaults.ClampRail);
            PlateListChanged?.Invoke(this, EventArgs.Empty);
            if (ReferenceEquals(CurrentPlate, trailing))
                FireCurrentPlateChanged();
        }

        public void BeginBatch()
        {
            batching = true;
        }

        public void EndBatch()
        {
            batching = false;
            EnsureSentinel();
            PlateListChanged?.Invoke(this, EventArgs.Empty);
            FireCurrentPlateChanged();
        }

        public Plate GetOrCreateEmpty()
        {
            for (var i = Count - 1; i >= 0; i--)
            {
                if (nest.Plates[i].Parts.Count == 0)
                    return nest.Plates[i];
            }

            return nest.CreatePlate();
        }

        public void RemoveCurrent()
        {
            if (Count < 2)
                return;

            nest.Plates.RemoveAt(CurrentIndex);
        }

        /// <summary>
        /// Whether <see cref="ClearAll"/> would change anything: more than one plate, or the
        /// remaining plate still holds parts or cut-off definitions.
        /// </summary>
        public bool CanClearAll
        {
            get
            {
                if (Count == 0)
                    return false;

                var remaining = nest.Plates[0];
                return Count > 1
                    || remaining.Parts.Count > 0
                    || remaining.CutOffs.Count > 0
                    || remaining.Quantity > 1;
            }
        }

        /// <summary>
        /// Clears the nest: removes every plate after the first and empties the remaining
        /// plate (parts and cut-off definitions), resetting its repeat quantity. Nest metadata
        /// and plate defaults are preserved; the first plate's configuration stays as the
        /// workspace sentinel — the same result as removing every other plate one at a time.
        /// The plate count never reaches zero, so observers never see a null current plate.
        /// </summary>
        public void ClearAll()
        {
            if (Count == 0)
                return;

            BeginBatch();
            try
            {
                while (nest.Plates.Count > 1)
                    nest.Plates.RemoveAt(nest.Plates.Count - 1);

                var remaining = nest.Plates[0];
                remaining.CutOffs.Clear();
                remaining.Parts.Clear();
                remaining.Quantity = 1;
                remaining.StockItemCodes = Array.Empty<string>();
            }
            finally
            {
                EndBatch();
            }
        }

        private void SubscribeToTailPlates()
        {
            UnsubscribeFromTailPlates();

            if (Count > 0)
            {
                subscribedLast = nest.Plates[^1];
                subscribedLast.PartAdded += OnTailPartAdded;
                subscribedLast.PartRemoved += OnTailPartRemoved;
            }

            if (Count > 1)
            {
                subscribedSecondToLast = nest.Plates[^2];
                subscribedSecondToLast.PartAdded += OnTailPartAdded;
                subscribedSecondToLast.PartRemoved += OnTailPartRemoved;
            }
        }

        private void UnsubscribeFromTailPlates()
        {
            if (subscribedLast != null)
            {
                subscribedLast.PartAdded -= OnTailPartAdded;
                subscribedLast.PartRemoved -= OnTailPartRemoved;
                subscribedLast = null;
            }

            if (subscribedSecondToLast != null)
            {
                subscribedSecondToLast.PartAdded -= OnTailPartAdded;
                subscribedSecondToLast.PartRemoved -= OnTailPartRemoved;
                subscribedSecondToLast = null;
            }
        }

        private void OnTailPartAdded(object sender, ItemAddedEventArgs<Part> e)
        {
            if (!batching)
                EnsureSentinel();
        }

        private void OnTailPartRemoved(object sender, ItemRemovedEventArgs<Part> e)
        {
            if (!batching)
                EnsureSentinel();
        }

        private void OnPlateAdded(object sender, ItemAddedEventArgs<Plate> e)
        {
            if (!suppressNavigation && !batching)
                EnsureSentinel();

            PlateListChanged?.Invoke(this, EventArgs.Empty);

            if (!suppressNavigation)
            {
                CurrentIndex = Count - 1;
                FireCurrentPlateChanged();
            }
        }

        private void OnPlateRemoved(object sender, ItemRemovedEventArgs<Plate> e)
        {
            if (CurrentIndex >= Count && Count > 0)
                CurrentIndex = Count - 1;

            if (!suppressNavigation && !batching)
                EnsureSentinel();

            PlateListChanged?.Invoke(this, EventArgs.Empty);

            if (!suppressNavigation)
                FireCurrentPlateChanged();
        }

        private void FireCurrentPlateChanged()
        {
            CurrentPlateChanged?.Invoke(
                this,
                new PlateChangedEventArgs(CurrentPlate, CurrentIndex)
            );
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            UnsubscribeFromTailPlates();
            nest.Plates.ItemAdded -= OnPlateAdded;
            nest.Plates.ItemRemoved -= OnPlateRemoved;
        }
    }
}
