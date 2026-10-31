using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Dimensions
{
    /// <summary>
    /// Resolves dimensions against the entities being cut (visible, with roles, breakoffs and
    /// tabbed cutouts applied), so a dimension measures the part as it will come off the table.
    /// Radius and diameter dimensions find their circle or arc by ID among the visible entities.
    /// </summary>
    public static class DimensionBuilder
    {
        private const double DefaultOffsetFactor = 0.1;
        private const double RadialOffsetFactor = 0.05;

        /// <summary>
        /// The extents of the geometry that bounds the part, or null if there is none. Source marks
        /// keep their own layer objects, so <see cref="SpecialLayers.IsMaterial"/> alone would count
        /// them; break cuts are skipped because they stay inside the outline they join.
        /// </summary>
        public static Box Extents(IEnumerable<Entity> entities)
        {
            var measured = Measured(entities);
            return measured.Count == 0 ? null : measured.GetBoundingBox();
        }

        private static List<Entity> Measured(IEnumerable<Entity> entities) =>
            entities
                .Where(e => SpecialLayers.IsMaterial(e.Layer) && !ScribeLayerRepair.IsMarkLayer(e.Layer))
                .ToList();

        public static DimensionLayout Layout(IEnumerable<Entity> entities, DrawingDimension dimension)
        {
            var kind = dimension.Kind;
            if (dimension.Offset == 0 || !double.IsFinite(dimension.Offset))
                return new DimensionLayout { Kind = kind, Error = "its offset is not usable" };

            if (dimension.IsRadial)
                return RadialLayout(entities, dimension);

            var measured = Measured(entities);
            if (measured.Count == 0)
                return new DimensionLayout { Kind = kind, Error = "there is no cut geometry to measure" };

            var box = measured.GetBoundingBox();

            var offset = dimension.Offset;
            if (kind == DimensionKind.OverallHorizontal)
            {
                if (box.Length <= 0)
                    return new DimensionLayout { Kind = kind, Error = "the part has no horizontal size" };

                var edge = offset > 0 ? box.Top : box.Bottom;
                return new DimensionLayout
                {
                    Kind = kind,
                    Extents = box,
                    Profile = measured,
                    Value = box.Length,
                    ExtensionStart = new Vector(box.Left, edge),
                    ExtensionEnd = new Vector(box.Right, edge),
                    LineStart = new Vector(box.Left, edge + offset),
                    LineEnd = new Vector(box.Right, edge + offset),
                };
            }

            if (box.Width <= 0)
                return new DimensionLayout { Kind = kind, Error = "the part has no vertical size" };

            var side = offset > 0 ? box.Right : box.Left;
            return new DimensionLayout
            {
                Kind = kind,
                Extents = box,
                Profile = measured,
                Value = box.Width,
                ExtensionStart = new Vector(side, box.Bottom),
                ExtensionEnd = new Vector(side, box.Top),
                LineStart = new Vector(side + offset, box.Bottom),
                LineEnd = new Vector(side + offset, box.Top),
            };
        }

        /// <summary>
        /// The overall dimensions <paramref name="existing"/> does not have yet, below and to the
        /// right of the part and a tenth of its larger size away. Empty when there is nothing to
        /// measure.
        /// </summary>
        public static List<DrawingDimension> MissingOverall(IEnumerable<Entity> entities,
            IEnumerable<DrawingDimension> existing)
        {
            var added = new List<DrawingDimension>();
            var box = Extents(entities);
            if (box == null)
                return added;

            var offset = System.Math.Max(box.Length, box.Width) * DefaultOffsetFactor;
            if (offset <= 0)
                return added;

            var kinds = existing.Select(d => d.Kind).ToHashSet();
            if (!kinds.Contains(DimensionKind.OverallHorizontal))
                added.Add(new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = -offset });
            if (!kinds.Contains(DimensionKind.OverallVertical))
                added.Add(new DrawingDimension { Kind = DimensionKind.OverallVertical, Offset = offset });
            return added;
        }

        /// <summary>
        /// A <paramref name="kind"/> dimension for each circle or arc in <paramref name="curves"/>
        /// that does not have one yet. On a circle the leader points up and right for a radius and
        /// up and left for a diameter, so the two do not overlap; on an arc it points through the
        /// middle. The label sits outside the curve by half its radius, or a twentieth of
        /// the part's larger size when that is more.
        /// </summary>
        public static List<DrawingDimension> MissingRadial(IEnumerable<Entity> curves, DimensionKind kind,
            IEnumerable<Entity> part, IEnumerable<DrawingDimension> existing)
        {
            var added = new List<DrawingDimension>();
            if (!DrawingDimension.IsRadialKind(kind))
                return added;

            var box = Extents(part);
            var partSize = box == null ? 0 : System.Math.Max(box.Length, box.Width);
            var taken = existing.Where(d => d.Kind == kind).Select(d => d.EntityId).ToHashSet();
            foreach (var curve in curves)
            {
                if (curve.Id == Guid.Empty || !taken.Add(curve.Id) || !TryCurve(curve, out _, out var radius, out var arc))
                    continue;
                added.Add(new DrawingDimension
                {
                    Kind = kind,
                    EntityId = curve.Id,
                    Angle = arc != null ? MidAngle(arc)
                        : kind == DimensionKind.Radius ? Angle.HalfPI / 2 : Angle.HalfPI * 1.5,
                    Offset = System.Math.Max(radius / 2, partSize * RadialOffsetFactor),
                });
            }
            return added;
        }

        /// <summary>Whether <paramref name="entity"/> is a circle or arc a radius or diameter can measure.</summary>
        public static bool IsRadialTarget(Entity entity) => TryCurve(entity, out _, out _, out _);

        private static bool TryCurve(Entity entity, out Vector center, out double radius, out Arc arc)
        {
            arc = null;
            switch (entity)
            {
                case Circle circle:
                    center = circle.Center;
                    radius = circle.Radius;
                    break;
                case Arc a:
                    center = a.Center;
                    radius = a.Radius;
                    arc = a.IsFullCircle() ? null : a;
                    break;
                default:
                    center = default;
                    radius = 0;
                    return false;
            }
            return radius > 0 && double.IsFinite(radius);
        }

        private static DimensionLayout RadialLayout(IEnumerable<Entity> entities, DrawingDimension dimension)
        {
            var kind = dimension.Kind;
            if (!double.IsFinite(dimension.Angle))
                return new DimensionLayout { Kind = kind, Error = "its leader angle is not usable" };

            var entity = dimension.EntityId == Guid.Empty
                ? null
                : entities.FirstOrDefault(e => e.Id == dimension.EntityId);
            if (entity == null)
                return new DimensionLayout { Kind = kind, Error = "its circle or arc is not cut" };
            if (!TryCurve(entity, out var center, out var radius, out var arc))
                return new DimensionLayout { Kind = kind, Error = "it no longer measures a circle or arc" };

            var reach = radius + dimension.Offset;
            if (reach <= 0)
                return new DimensionLayout { Kind = kind, Error = "its offset is not usable" };

            var angle = arc != null && kind == DimensionKind.Radius
                ? ClampToSweep(arc, dimension.Angle)
                : dimension.Angle;
            var direction = new Vector(System.Math.Cos(angle), System.Math.Sin(angle));
            var arrow = center + direction * radius;
            var far = center - direction * radius;
            return new DimensionLayout
            {
                Kind = kind,
                Value = kind == DimensionKind.Diameter ? radius * 2 : radius,
                Center = center,
                Radius = radius,
                Angle = angle,
                Arc = arc,
                ArrowPoint = arrow,
                FarArrowPoint = far,
                LineStart = kind == DimensionKind.Diameter ? far : center,
                LineEnd = center + direction * reach,
                Extents = new Box(center.X - radius, center.Y - radius, radius * 2, radius * 2),
            };
        }

        /// <summary><paramref name="angle"/> if it is on the arc's sweep, else the nearer end's angle.</summary>
        public static double ClampToSweep(Arc arc, double angle)
        {
            if (Angle.IsBetweenRad(angle, arc.StartAngle, arc.EndAngle, arc.IsReversed))
                return angle;
            var toStart = AngularDistance(angle, arc.StartAngle);
            var toEnd = AngularDistance(angle, arc.EndAngle);
            return toStart <= toEnd ? arc.StartAngle : arc.EndAngle;
        }

        private static double AngularDistance(double a, double b)
        {
            var d = Angle.NormalizeRad(a - b);
            return System.Math.Min(d, Angle.TwoPI - d);
        }

        private static double MidAngle(Arc arc)
        {
            // SweepAngle runs counterclockwise from the start, or from the end on a reversed arc.
            var from = arc.IsReversed ? arc.EndAngle : arc.StartAngle;
            return Angle.NormalizeRad(from + arc.SweepAngle() / 2);
        }
    }
}
