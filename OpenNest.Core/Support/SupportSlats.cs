using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Support
{
    /// <summary>
    /// A bed of straight slats, as on a plasma table: each slat a rectangle <see cref="Thickness"/>
    /// wide running the full length of the table along <see cref="Axis"/>, one every
    /// <see cref="Pitch"/> across it, the first centered <see cref="Offset"/> from the plate origin
    /// into the sheet. A sheet in another quadrant sees the same slats mirrored. A part rests on
    /// the region where its material crosses a slat; the corners of that region (where the
    /// outline crosses a slat's edges, and outline vertices on the slat) are its contacts.
    /// Lengths are in the nest's units.
    /// </summary>
    public sealed class SupportSlats : SupportBed
    {
        public SupportSlats(SlatAxis axis, double pitch, double thickness, double offset = 0,
            double registrationTolerance = 0, double stabilityMargin = 0)
            : base(registrationTolerance, stabilityMargin)
        {
            if (axis is not (SlatAxis.X or SlatAxis.Y))
                throw new ArgumentOutOfRangeException(nameof(axis), "Slats run along X or Y.");
            if (!double.IsFinite(pitch) || pitch <= 0)
                throw new ArgumentOutOfRangeException(nameof(pitch), "Slat pitch must be finite and positive.");
            if (!double.IsFinite(thickness) || thickness < 0)
                throw new ArgumentOutOfRangeException(nameof(thickness), "Slat thickness must be finite and zero or more.");
            if (!double.IsFinite(offset))
                throw new ArgumentOutOfRangeException(nameof(offset), "Slat offset must be finite.");
            Axis = axis;
            Pitch = pitch;
            Thickness = thickness;
            Offset = offset;
        }

        public SlatAxis Axis { get; }

        /// <summary>The distance between neighboring slats' center lines.</summary>
        public double Pitch { get; }

        /// <summary>How wide each slat's top edge is; zero treats a slat as a line.</summary>
        public double Thickness { get; }

        /// <summary>The first slat's center line, measured from the plate origin into the sheet.</summary>
        public double Offset { get; }

        public override IReadOnlyList<Vector> Contacts(SupportMaterial material, int quadrant, Vector shift,
            bool extremesOnly = false)
        {
            ArgumentNullException.ThrowIfNull(material);
            var contacts = new List<Vector>();
            if (!material.IsComplete)
                return contacts;

            // Work across the slats: Y for slats along X, X for slats along Y.
            var alongX = Axis == SlatAxis.X;
            var baseline = alongX ? SignY(quadrant) * Offset + shift.Y : SignX(quadrant) * Offset + shift.X;
            var bounds = material.Bounds;
            var low = alongX ? bounds.Bottom : bounds.Left;
            var high = alongX ? bounds.Top : bounds.Right;
            var half = Thickness / 2;
            var first = (long)System.Math.Ceiling((low - half - baseline) / Pitch);
            var last = (long)System.Math.Floor((high + half - baseline) / Pitch);
            if (first > last)
                return contacts;

            // Where the outline crosses each slat's edges.
            for (var slat = first; slat <= last; slat++)
            {
                var center = baseline + slat * Pitch;
                AddEdge(material, alongX, center - half, contacts);
                if (half > 0)
                    AddEdge(material, alongX, center + half, contacts);
            }

            // Outline and hole vertices that sit on a slat: every vertex on one slat's top.
            AddVertices(material.Outline, alongX, baseline, half, contacts);
            foreach (var hole in material.Holes)
                AddVertices(hole, alongX, baseline, half, contacts);
            return contacts;
        }

        /// <summary>
        /// The slats within <paramref name="region"/> (world coordinates) on a sheet in
        /// <paramref name="quadrant"/>, each cut off to the region, for display; empty when there
        /// would be more than <paramref name="limit"/>.
        /// </summary>
        public IReadOnlyList<Box> SlatsIn(Box region, int quadrant, int limit = 5000)
        {
            ArgumentNullException.ThrowIfNull(region);
            var alongX = Axis == SlatAxis.X;
            var baseline = alongX ? SignY(quadrant) * Offset : SignX(quadrant) * Offset;
            var low = alongX ? region.Bottom : region.Left;
            var high = alongX ? region.Top : region.Right;
            var half = Thickness / 2;
            var first = System.Math.Ceiling((low - half - baseline) / Pitch);
            var last = System.Math.Floor((high + half - baseline) / Pitch);
            var count = last - first + 1;
            if (!double.IsFinite(count) || count <= 0 || count > limit)
                return Array.Empty<Box>();

            var slats = new List<Box>((int)count);
            for (var slat = first; slat <= last; slat++)
            {
                var center = baseline + slat * Pitch;
                var start = System.Math.Max(low, center - half);
                var end = System.Math.Min(high, center + half);
                slats.Add(alongX
                    ? new Box(region.Left, start, region.Length, end - start)
                    : new Box(start, region.Bottom, end - start, region.Width));
            }
            return slats;
        }

        public override SupportBed Scaled(double factor) =>
            new SupportSlats(Axis, Pitch * factor, Thickness * factor, Offset * factor,
                RegistrationTolerance * factor, StabilityMargin * factor);

        public override bool Equals(SupportBed other) =>
            other is SupportSlats slats
            && slats.Axis == Axis && slats.Pitch == Pitch
            && slats.Thickness == Thickness && slats.Offset == Offset
            && slats.RegistrationTolerance == RegistrationTolerance
            && slats.StabilityMargin == StabilityMargin;

        public override int GetHashCode() =>
            HashCode.Combine(Axis, Pitch, Thickness, Offset, RegistrationTolerance, StabilityMargin);

        private static void AddEdge(SupportMaterial material, bool alongX, double across, List<Vector> contacts)
        {
            var spans = alongX ? material.SpansAtY(across) : material.SpansAtX(across);
            foreach (var (start, end) in spans)
            {
                contacts.Add(alongX ? new Vector(start, across) : new Vector(across, start));
                contacts.Add(alongX ? new Vector(end, across) : new Vector(across, end));
            }
        }

        private void AddVertices(IReadOnlyList<Vector> ring, bool alongX, double baseline, double half,
            List<Vector> contacts)
        {
            foreach (var vertex in ring)
            {
                var across = alongX ? vertex.Y : vertex.X;
                // The nearest slat center; the vertex is on that slat when within half its thickness.
                var center = baseline + System.Math.Round((across - baseline) / Pitch) * Pitch;
                if (System.Math.Abs(across - center) <= half)
                    contacts.Add(vertex);
            }
        }
    }
}
