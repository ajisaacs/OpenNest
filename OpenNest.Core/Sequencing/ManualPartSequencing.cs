using System;
using System.Linq;

namespace OpenNest.Sequencing
{
    /// <summary>Moves one existing part to a one-based cutting sequence position.</summary>
    public static class ManualPartSequencing
    {
        public static bool Move(Plate plate, Part part, int sequenceNumber)
        {
            ArgumentNullException.ThrowIfNull(plate);
            ArgumentNullException.ThrowIfNull(part);
            var order = plate.Parts.ToList();
            if (sequenceNumber < 1 || sequenceNumber > order.Count)
                return false;

            var oldIndex = order.FindIndex(p => ReferenceEquals(p, part));
            if (oldIndex < 0)
                return false;
            if (oldIndex == sequenceNumber - 1)
                return true;

            order.RemoveAt(oldIndex);
            order.Insert(sequenceNumber - 1, part);
            plate.Parts.Reorder(order);
            return true;
        }
    }
}
