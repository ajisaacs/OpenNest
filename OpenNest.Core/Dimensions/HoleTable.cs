using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Dimensions
{
    /// <summary>One hole diameter in a <see cref="HoleTable"/> and every hole cut at that size.</summary>
    public sealed class HoleSize
    {
        public HoleSize(double diameter, bool isKeyed, IReadOnlyList<Vector> centers, IReadOnlyList<Guid> entityIds)
        {
            Diameter = diameter;
            IsKeyed = isKeyed;
            Centers = centers;
            EntityIds = entityIds;
        }

        /// <summary>The diameter, rounded to <see cref="HoleTable.Decimals"/> places.</summary>
        public double Diameter { get; }

        /// <summary>True when the holes are round except for a keyway, flat or other interruption.</summary>
        public bool IsKeyed { get; }

        public int Quantity => Centers.Count;

        public IReadOnlyList<Vector> Centers { get; }

        /// <summary>The IDs of every entity that makes up a hole of this size.</summary>
        public IReadOnlyList<Guid> EntityIds { get; }

        public string DiameterText => "\u00D8" + Diameter.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
            + (IsKeyed ? " keyed" : "");
    }

    /// <summary>
    /// Counts the round and keyed holes in a drawing by diameter. A round hole is a cut circle, or a
    /// closed contour made only of arcs on one center and radius; a keyed hole is a closed contour
    /// around its center with at least <see cref="MinKeyedSweep"/> of arc on one center and radius,
    /// and anything else for the rest. Either must lie inside another cut contour, so the outline of
    /// a round part is not a hole. Marks (scribe, etch, rapids, break cuts) are not holes. Pass the
    /// entities as they will be cut (visible, with roles applied).
    /// </summary>
    public static class HoleTable
    {
        /// <summary>Holes whose diameters agree to this many decimal places share a row.</summary>
        public const int Decimals = 4;

        /// <summary>The least arc, in radians, that makes a contour a keyed hole: 270 degrees.</summary>
        public const double MinKeyedSweep = 1.5 * System.Math.PI;

        private const double SweepTolerance = 1e-6;

        public static IReadOnlyList<HoleSize> Build(IEnumerable<Entity> entities)
        {
            var cut = entities
                .Where(e => SpecialLayers.IsMaterial(e.Layer) && !ScribeLayerRepair.IsMarkLayer(e.Layer))
                .ToList();
            if (cut.Count == 0)
                return Array.Empty<HoleSize>();

            var shapes = ShapeBuilder.GetShapes(cut);
            var holes = new List<(double Diameter, bool Keyed, Vector Center, List<Guid> Ids)>();
            foreach (var shape in shapes)
            {
                if (!TryRound(shape, out var center, out var radius, out var keyed))
                    continue;
                var box = shape.BoundingBox;
                var area = box.Area();
                if (!shapes.Any(other => !ReferenceEquals(other, shape)
                        && other.BoundingBox.Area() > area + Tolerance.Epsilon
                        && other.BoundingBox.Contains(box)))
                    continue;
                holes.Add((System.Math.Round(radius * 2, Decimals), keyed, center, shape.Entities.Select(e => e.Id).ToList()));
            }

            return holes
                .GroupBy(h => (h.Diameter, h.Keyed))
                .OrderBy(g => g.Key.Diameter)
                .ThenBy(g => g.Key.Keyed)
                .Select(g => new HoleSize(
                    g.Key.Diameter,
                    g.Key.Keyed,
                    g.Select(h => h.Center).ToList(),
                    g.SelectMany(h => h.Ids).ToList()))
                .ToList();
        }

        private static bool TryRound(Shape shape, out Vector center, out double radius, out bool keyed)
        {
            center = default;
            radius = 0;
            keyed = false;
            if (shape.Entities.Count == 1 && shape.Entities[0] is Circle circle)
            {
                center = circle.Center;
                radius = circle.Radius;
                return radius > 0;
            }

            if (!shape.IsClosed())
                return false;

            var arcs = shape.Entities.OfType<Arc>().ToList();
            var tolerance = Tolerance.ChainTolerance;
            var sweep = 0.0;
            foreach (var arc in arcs)
            {
                var total = arcs
                    .Where(a => a.Center.DistanceTo(arc.Center) <= tolerance
                        && System.Math.Abs(a.Radius - arc.Radius) <= tolerance)
                    .Sum(a => a.SweepAngle());
                if (total <= sweep)
                    continue;
                sweep = total;
                center = arc.Center;
                radius = arc.Radius;
            }

            if (radius <= 0 || sweep > Angle.TwoPI + SweepTolerance)
                return false;

            if (System.Math.Abs(sweep - Angle.TwoPI) <= SweepTolerance && arcs.Count == shape.Entities.Count)
                return true;

            // The center test turns away a curved slot, whose long arcs wrap around solid material.
            keyed = true;
            return sweep >= MinKeyedSweep - SweepTolerance && shape.ToPolygon().ContainsPoint(center);
        }
    }
}
