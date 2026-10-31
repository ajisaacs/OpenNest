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
        public HoleSize(double diameter, IReadOnlyList<Vector> centers, IReadOnlyList<Guid> entityIds)
        {
            Diameter = diameter;
            Centers = centers;
            EntityIds = entityIds;
        }

        /// <summary>The diameter, rounded to <see cref="HoleTable.Decimals"/> places.</summary>
        public double Diameter { get; }

        public int Quantity => Centers.Count;

        public IReadOnlyList<Vector> Centers { get; }

        /// <summary>The IDs of every entity that makes up a hole of this size.</summary>
        public IReadOnlyList<Guid> EntityIds { get; }

        public string DiameterText => "\u00D8" + Diameter.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Counts the round holes in a drawing by diameter. A round hole is a cut circle, or a closed
    /// contour made only of arcs on one center and radius, that lies inside another cut contour, so
    /// the outline of a round part is not a hole. Marks (scribe, etch, rapids, break cuts) are not
    /// holes. Pass the entities as they will be cut (visible, with roles applied).
    /// </summary>
    public static class HoleTable
    {
        /// <summary>Holes whose diameters agree to this many decimal places share a row.</summary>
        public const int Decimals = 4;

        public static IReadOnlyList<HoleSize> Build(IEnumerable<Entity> entities)
        {
            var cut = entities
                .Where(e => SpecialLayers.IsMaterial(e.Layer) && !ScribeLayerRepair.IsMarkLayer(e.Layer))
                .ToList();
            if (cut.Count == 0)
                return Array.Empty<HoleSize>();

            var shapes = ShapeBuilder.GetShapes(cut);
            var holes = new List<(double Diameter, Vector Center, List<Guid> Ids)>();
            foreach (var shape in shapes)
            {
                if (!TryRound(shape, out var center, out var radius))
                    continue;
                var box = shape.BoundingBox;
                var area = box.Area();
                if (!shapes.Any(other => !ReferenceEquals(other, shape)
                        && other.BoundingBox.Area() > area + Tolerance.Epsilon
                        && other.BoundingBox.Contains(box)))
                    continue;
                holes.Add((System.Math.Round(radius * 2, Decimals), center, shape.Entities.Select(e => e.Id).ToList()));
            }

            return holes
                .GroupBy(h => h.Diameter)
                .OrderBy(g => g.Key)
                .Select(g => new HoleSize(
                    g.Key,
                    g.Select(h => h.Center).ToList(),
                    g.SelectMany(h => h.Ids).ToList()))
                .ToList();
        }

        private static bool TryRound(Shape shape, out Vector center, out double radius)
        {
            center = default;
            radius = 0;
            if (shape.Entities.Count == 1 && shape.Entities[0] is Circle circle)
            {
                center = circle.Center;
                radius = circle.Radius;
                return radius > 0;
            }

            if (shape.Entities.Count == 0 || !shape.Entities.All(e => e is Arc) || !shape.IsClosed())
                return false;

            var arcs = shape.Entities.Cast<Arc>().ToList();
            var first = arcs[0];
            var tolerance = Tolerance.ChainTolerance;
            if (arcs.Any(a => a.Center.DistanceTo(first.Center) > tolerance
                    || System.Math.Abs(a.Radius - first.Radius) > tolerance))
                return false;
            if (System.Math.Abs(arcs.Sum(a => a.SweepAngle()) - Angle.TwoPI) > 1e-6)
                return false;

            center = first.Center;
            radius = first.Radius;
            return radius > 0;
        }
    }
}
