using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    /// <summary>
    /// Catalog of standard mill sheet sizes (inches) with helpers for matching
    /// a bounding box to a recommended plate size. Uses the project-wide
    /// (Width, Length) convention where Width is the short dimension and
    /// Length is the long dimension.
    /// </summary>
    public static class PlateSizes
    {
        public readonly record struct Entry(string Label, double Width, double Length)
        {
            public double Area => Width * Length;

            /// <summary>
            /// Returns true if a part of the given dimensions fits within this entry
            /// in either orientation.
            /// </summary>
            public bool Fits(double width, double length) =>
                (width <= Width && length <= Length) || (width <= Length && length <= Width);
        }

        /// <summary>
        /// Standard mill sheet sizes (inches), sorted by area ascending.
        /// Canonical orientation: Width &lt;= Length.
        /// </summary>
        public static IReadOnlyList<Entry> All { get; } = new[]
        {
            new Entry("48x96",   48,  96),   // 4608
            new Entry("48x120",  48, 120),   // 5760
            new Entry("48x144",  48, 144),   // 6912
            new Entry("60x120",  60, 120),   // 7200
            new Entry("60x144",  60, 144),   // 8640
            new Entry("72x120",  72, 120),   // 8640
            new Entry("72x144",  72, 144),   // 10368
            new Entry("96x240",  96, 240),   // 23040
        };

        /// <summary>
        /// Looks up a standard size by label. Case-insensitive.
        /// </summary>
        public static bool TryGet(string label, out Entry entry)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                foreach (var candidate in All)
                {
                    if (string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// Recommends a plate size for the given bounding box. The box's
        /// spatial axes are normalized to (short, long) so neither the bbox
        /// orientation nor Box's internal Length/Width naming matters.
        /// </summary>
        public static PlateSizeResult Recommend(Box bbox, PlateSizeOptions options = null)
        {
            var a = bbox.Width;
            var b = bbox.Length;
            return Recommend(System.Math.Min(a, b), System.Math.Max(a, b), options);
        }

        /// <summary>
        /// Recommends a plate size for the envelope of the given boxes.
        /// </summary>
        public static PlateSizeResult Recommend(IEnumerable<Box> boxes, PlateSizeOptions options = null)
        {
            if (boxes == null)
                throw new ArgumentNullException(nameof(boxes));

            var hasAny = false;
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;

            foreach (var box in boxes)
            {
                hasAny = true;
                if (box.Left < minX) minX = box.Left;
                if (box.Bottom < minY) minY = box.Bottom;
                if (box.Right > maxX) maxX = box.Right;
                if (box.Top > maxY) maxY = box.Top;
            }

            if (!hasAny)
                throw new ArgumentException("At least one box is required.", nameof(boxes));

            var b = maxX - minX;
            var a = maxY - minY;
            return Recommend(System.Math.Min(a, b), System.Math.Max(a, b), options);
        }

        /// <summary>
        /// Recommends a plate size for a (width, length) pair.
        /// Inputs are treated as orientation-independent.
        /// </summary>
        public static PlateSizeResult Recommend(double width, double length, PlateSizeOptions options = null)
        {
            options ??= new PlateSizeOptions();

            var w = width + 2 * options.Margin;
            var l = length + 2 * options.Margin;

            // Canonicalize (short, long) — Fits handles rotation anyway, but
            // normalizing lets the below-min comparison use the narrower
            // MinSheet dimensions consistently.
            if (w > l)
                (w, l) = (l, w);

            // Below full-sheet threshold: snap each dimension up to the nearest increment.
            if (w <= options.MinSheetWidth && l <= options.MinSheetLength)
                return SnapResult(w, l, options.SnapIncrement);

            var catalog = BuildCatalog(options.AllowedSizes);

            var best = PickBest(catalog, w, l, options.Selection);
            if (best.HasValue)
                return new PlateSizeResult(best.Value.Width, best.Value.Length, best.Value.Label);

            // Nothing in the catalog fits - fall back to snap-up (ad-hoc oversize sheet).
            return SnapResult(w, l, options.SnapIncrement);
        }

        private static PlateSizeResult SnapResult(double width, double length, double increment)
        {
            if (increment <= 0)
                return new PlateSizeResult(width, length, null);

            return new PlateSizeResult(SnapUp(width, increment), SnapUp(length, increment), null);
        }

        private static double SnapUp(double value, double increment)
        {
            var steps = System.Math.Ceiling(value / increment);
            return steps * increment;
        }

        private static IReadOnlyList<Entry> BuildCatalog(IReadOnlyList<string> allowedSizes)
        {
            if (allowedSizes == null || allowedSizes.Count == 0)
                return All;

            var result = new List<Entry>(allowedSizes.Count);
            foreach (var label in allowedSizes)
            {
                if (TryParseEntry(label, out var entry))
                    result.Add(entry);
            }

            return result;
        }

        private static bool TryParseEntry(string label, out Entry entry)
        {
            if (TryGet(label, out entry))
                return true;

            // Accept ad-hoc "WxL" strings (e.g. "50x100", "50 x 100").
            if (!string.IsNullOrWhiteSpace(label))
            {
                var parts = label.Split(new[] { 'x', 'X' }, 2);
                if (parts.Length == 2
                    && double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)
                    && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)
                    && a > 0 && b > 0)
                {
                    var width = System.Math.Min(a, b);
                    var length = System.Math.Max(a, b);
                    entry = new Entry(label.Trim(), width, length);
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private static Entry? PickBest(IReadOnlyList<Entry> catalog, double width, double length, PlateSizeSelection selection)
        {
            var fitting = catalog.Where(e => e.Fits(width, length));

            fitting = selection switch
            {
                PlateSizeSelection.NarrowestFirst => fitting.OrderBy(e => e.Width).ThenBy(e => e.Area),
                _ => fitting.OrderBy(e => e.Area).ThenBy(e => e.Width),
            };

            foreach (var candidate in fitting)
                return candidate;

            return null;
        }
    }

    public readonly record struct PlateSizeResult(double Width, double Length, string MatchedLabel)
    {
        public bool IsStandard => MatchedLabel != null;
    }

    public sealed class PlateSizeOptions
    {
        /// <summary>
        /// If the margin-adjusted bounding box fits within MinSheetWidth x MinSheetLength
        /// the result is snapped to <see cref="SnapIncrement"/> instead of routed to a
        /// standard sheet. Default 48" x 48".
        /// </summary>
        public double MinSheetWidth { get; set; } = 48;
        public double MinSheetLength { get; set; } = 48;

        /// <summary>
        /// Increment used for below-threshold rounding and oversize fallback. Default 1".
        /// </summary>
        public double SnapIncrement { get; set; } = 1.0;

        /// <summary>
        /// Extra clearance added to each side of the bounding box before matching.
        /// </summary>
        public double Margin { get; set; } = 0;

        /// <summary>
        /// Optional whitelist. When non-empty, only these sizes are considered.
        /// Entries may be standard catalog labels (e.g. "48x96") or arbitrary
        /// "WxL" strings (e.g. "50x100").
        /// </summary>
        public IReadOnlyList<string> AllowedSizes { get; set; }

        /// <summary>
        /// Tiebreaker when multiple sheets can contain the bounding box.
        /// </summary>
        public PlateSizeSelection Selection { get; set; } = PlateSizeSelection.SmallestArea;
    }

    public enum PlateSizeSelection
    {
        /// <summary>Pick the cheapest sheet that contains the bbox (smallest area).</summary>
        SmallestArea,
        /// <summary>Prefer narrower-width sheets (e.g. 48-wide before 60-wide).</summary>
        NarrowestFirst,
    }
}
