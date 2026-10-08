using System.Collections.Generic;

namespace OpenNest
{
    /// <summary>
    /// Display-only plate numbering for labels. The editor keeps one trailing empty plate as the
    /// "new plate" workspace (PlateManager.EnsureSentinel), and that sentinel is excluded from the
    /// shown total so a one-plate nest reads "Plate 1 of 1". Navigation, storage indexes, exported
    /// names and batch selection keep using the real collection positions; an interior empty plate
    /// keeps its slot and its number.
    /// </summary>
    public static class PlateDisplayNumbering
    {
        /// <summary>
        /// The number shown for the plate at <paramref name="storageIndex"/>: real collection
        /// positions count as 1-based plate numbers. Returns null for the trailing "new plate"
        /// sentinel and for indexes outside the collection.
        /// </summary>
        public static int? DisplayedPlateNumber(IList<Plate> plates, int storageIndex)
        {
            if (plates == null || storageIndex < 0 || storageIndex >= plates.Count)
                return null;
            if (IsTrailingSentinel(plates, storageIndex))
                return null;
            return storageIndex + 1;
        }

        /// <summary>
        /// The number shown in an "of M" total: the plates minus only a trailing empty sentinel.
        /// An all-empty collection shows nothing (the caller says "No plates").
        /// </summary>
        public static int DisplayedPlateCount(IList<Plate> plates)
        {
            if (plates == null || plates.Count == 0)
                return 0;
            var last = plates[plates.Count - 1];
            return plates.Count - (last != null && last.Parts.Count == 0 ? 1 : 0);
        }

        /// <summary>True when <paramref name="storageIndex"/> is the trailing empty new-plate sentinel.</summary>
        public static bool IsTrailingSentinel(IList<Plate> plates, int storageIndex)
        {
            if (plates == null || plates.Count == 0)
                return false;
            if (storageIndex != plates.Count - 1)
                return false;
            var plate = plates[storageIndex];
            return plate != null && plate.Parts.Count == 0;
        }

        /// <summary>Header text for the plate at <paramref name="storageIndex"/>.</summary>
        public static string FormatHeader(IList<Plate> plates, int storageIndex, string plateSizeText)
        {
            var displayed = DisplayedPlateNumber(plates, storageIndex);
            if (displayed == null)
                return IsTrailingSentinel(plates, storageIndex)
                    ? "New plate (empty)"
                    : "No plates";
            return string.IsNullOrEmpty(plateSizeText)
                ? string.Format("Plate {0} of {1}", displayed.Value, DisplayedPlateCount(plates))
                : string.Format("Plate {0} of {1}  |  {2}", displayed.Value, DisplayedPlateCount(plates), plateSizeText);
        }
    }
}
