using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Engine
{
    /// <summary>
    /// Produces transient canonical (MBR-axis-aligned) copies of drawings for engine consumption
    /// and un-rotates placed parts back to the drawing's original frame.
    /// </summary>
    public static class CanonicalFrame
    {
        /// <summary>
        /// Returns a new Drawing whose Program geometry is rotated to the canonical frame.
        /// The source drawing is not mutated.
        /// </summary>
        public static Drawing AsCanonicalCopy(Drawing drawing)
        {
            if (drawing == null)
                return null;

            var angle = drawing.Source?.Angle ?? 0.0;

            // Clone program (never mutate the source).
            var pgm = (drawing.Program.Clone() as OpenNest.CNC.Program)
                      ?? new OpenNest.CNC.Program();

            if (!Tolerance.IsEqualTo(angle, 0))
                pgm.Rotate(angle, pgm.BoundingBox().Center);

            var copy = new Drawing(drawing.Name ?? string.Empty, pgm)
            {
                Color = drawing.Color,
                Constraints = drawing.Constraints,
                Material = drawing.Material,
                Priority = drawing.Priority,
                Customer = drawing.Customer,
                IsCutOff = drawing.IsCutOff,
                Source = new SourceInfo
                {
                    Path = drawing.Source?.Path,
                    Offset = drawing.Source?.Offset ?? new Vector(0, 0),
                    Angle = 0.0,
                },
            };
            return copy;
        }

        /// <summary>
        /// Composes the source drawing's canonical angle onto each placed part so the
        /// returned list is in the drawing's original (visible) frame.
        ///
        /// Derivation: let sourceAngle = S (rotation mapping source -> canonical).
        /// Canonical part at rotation R shows visible orientation R.
        /// Source part at rotation R' shows visible orientation R' + (-S), because the
        /// source geometry is already rotated by -S relative to canonical.
        /// Setting equal gives R' = R + S, so we ADD sourceAngle to each placed part.
        ///
        /// Rotation is performed around the part's Location so its placement position is preserved;
        /// only the orientation composes.
        /// </summary>
        public static List<Part> FromCanonical(List<Part> placed, double sourceAngle)
        {
            if (placed == null || placed.Count == 0)
                return placed;
            if (Tolerance.IsEqualTo(sourceAngle, 0))
                return placed;

            foreach (var p in placed)
                p.Rotate(sourceAngle, p.Location);

            return placed;
        }
    }
}
