using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Mcp
{
    public class NestSession
    {
        private Nest nest;

        public Nest Nest
        {
            get => nest;
            set
            {
                nest = value;
                ReconcileStockReservations();
            }
        }
        public List<Plate> Plates { get; } = new();
        public List<Drawing> Drawings { get; } = new();

        // A repeated layout owns its otherwise-empty physical sheet placeholders.
        // Clearing/removing that owner releases only its copies, never another layout's.
        // User-authored plates and pre-existing quantities imply no reservations.
        private readonly Dictionary<Plate, HashSet<Plate>> stockReservations = new(ReferenceEqualityComparer.Instance);

        internal bool IsConsumedStockPlate(Plate plate)
        {
            ReconcileStockReservations();
            return stockReservations.Values.Any(copies => copies.Contains(plate));
        }

        internal void ReserveStockCopies(Plate owner, IEnumerable<Plate> copies)
        {
            var reserved = new HashSet<Plate>(copies, ReferenceEqualityComparer.Instance);
            if (reserved.Count > 0)
                stockReservations[owner] = reserved;
        }

        internal void ReleaseStockCopies(Plate owner) => stockReservations.Remove(owner);

        private void ReconcileStockReservations()
        {
            if (stockReservations.Count == 0)
                return;

            // Both collections are publicly mutable. Reconcile by reference, not index,
            // so removals/replacements cannot retain stale plates or free unrelated stock.
            var live = new HashSet<Plate>(Plates, ReferenceEqualityComparer.Instance);
            if (Nest != null)
                live.UnionWith(Nest.Plates);
            foreach (var (owner, copies) in stockReservations.ToArray())
            {
                if (!live.Contains(owner) || owner.Parts.Count == 0)
                {
                    stockReservations.Remove(owner);
                    continue;
                }
                copies.RemoveWhere(copy => !live.Contains(copy));
                if (copies.Count == 0)
                    stockReservations.Remove(owner);
            }
        }

        /// <summary>
        /// Whole-job engine <c>autonest_plate</c> uses when a call does not pass an explicit
        /// <c>engine</c> argument; fill tools default to the Fill strategy instead. Instance state
        /// only — the process-global legacy registry is never read or written.
        /// </summary>
        public string DefaultEngineName { get; set; } = "Default";

        public Plate GetPlate(int index)
        {
            if (index < 0)
                return null;

            if (Nest != null && index < Nest.Plates.Count)
                return Nest.Plates[index];

            var adjustedIndex = index - (Nest?.Plates.Count ?? 0);
            if (adjustedIndex >= 0 && adjustedIndex < Plates.Count)
                return Plates[adjustedIndex];

            return null;
        }

        /// <summary>Remove only a genuinely unused sheet; loaded sheets precede appended sheets.</summary>
        public bool TryDeleteEmptyPlate(int index, out string reason)
        {
            var plate = GetPlate(index);
            if (plate == null)
            {
                reason = $"plate {index} not found";
                return false;
            }

            if (plate.Parts.Count != 0)
            {
                reason = $"plate {index} is occupied";
                return false;
            }

            if (plate.CutOffs.Count != 0)
            {
                reason = $"plate {index} has cutoff definitions";
                return false;
            }

            var loadedCount = Nest?.Plates.Count ?? 0;
            if (index < loadedCount)
                Nest.Plates.RemoveAt(index);
            else
                Plates.RemoveAt(index - loadedCount);

            ReconcileStockReservations();
            reason = null;
            return true;
        }

        public Drawing GetDrawing(string name)
        {
            if (Nest != null)
            {
                foreach (var d in Nest.Drawings)
                {
                    if (d.Name == name)
                        return d;
                }
            }

            foreach (var d in Drawings)
            {
                if (d.Name == name)
                    return d;
            }

            return null;
        }

        public List<Plate> AllPlates()
        {
            ReconcileStockReservations();
            var all = new List<Plate>();
            if (Nest != null)
                all.AddRange(Nest.Plates);
            all.AddRange(Plates);
            return all;
        }

        public List<Drawing> AllDrawings()
        {
            var all = new List<Drawing>();
            if (Nest != null)
                all.AddRange(Nest.Drawings);
            all.AddRange(Drawings);
            return all;
        }
    }
}
