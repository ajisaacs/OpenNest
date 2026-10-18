using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.CirclePacking
{
    /// <summary>
    /// A part as the circle packer sees it: its material outline's minimum enclosing circle, and
    /// the largest disc inside it that is clear of material. Any shape can be packed this way; a
    /// low <see cref="FillRatio"/> only means the circle wastes more around the part. Radii are
    /// what the layout check sees, so packing is never more lenient than the check: the outline
    /// circle covers the check's outward-flattened perimeter, and the hole stays inside its
    /// inward-flattened cutouts.
    /// </summary>
    internal class Item : Circle
    {
        /// <summary>How far the layout check's flattening may move a contour from its arcs.</summary>
        public const double FlattenAllowance = NestTolerances.ValidationOutline;

        // Optional caller-supplied packing identifier; not assigned automatically; preserved by clones.
        public int PackingId { get; set; }

        /// <summary>Material outline area divided by the enclosing circle's area: 1 for a disc,
        /// about 0.85 for a gear, 2/pi for a square. 0 when unknown.</summary>
        public double FillRatio { get; set; }

        /// <summary>From <see cref="Circle.Center"/> to the part's local origin, unrotated.
        /// The part's location is <c>Center + PartOffset</c> when it is placed without rotation.</summary>
        public Vector PartOffset { get; set; }

        /// <summary>From <see cref="Circle.Center"/> to the clear disc's center, unrotated.</summary>
        public Vector HoleOffset { get; set; }

        /// <summary>Radius of the largest disc clear of material; 0 for a solid part.</summary>
        public double HoleRadius { get; set; }

        /// <summary>The clear disc where it currently sits, or null for a solid part.</summary>
        public Circle Hole => HoleRadius > 0 ? new Circle(Center + HoleOffset, HoleRadius) : null;

        public static Item TryCreate(PartGeometrySnapshot snapshot) =>
            JobPartGeometry.TryRead(snapshot) is { } geometry ? Create(geometry) : null;

        /// <summary>Builds the item in the part's unrotated local frame.</summary>
        public static Item Create(JobPartGeometry geometry)
        {
            // Every point of the check's outline lies within the allowance of the analytic curve,
            // which the circumscribed flattening already contains.
            var perimeter = geometry.Perimeter.ToPolygonWithTolerance(FlattenAllowance, circumscribe: true);
            var enclosing = MinimumEnclosingCircle.Compute(perimeter.Vertices);
            var radius = enclosing.Radius + FlattenAllowance;
            var center = enclosing.Center;

            var item = new Item
            {
                Center = center,
                Radius = radius,
                FillRatio = System.Math.Abs(geometry.Perimeter.Area()) / (System.Math.PI * radius * radius),
                PartOffset = -center,
            };

            foreach (var cutout in geometry.Cutouts)
            {
                var polygon = cutout.ToPolygonWithTolerance(FlattenAllowance);
                var candidates = new[] { PolyLabel.Find(polygon, precision: FlattenAllowance), center };

                foreach (var candidate in candidates.Where(polygon.ContainsPoint))
                {
                    // Measured to the analytic boundaries, so it holds however the check flattens
                    // them; every cutout counts, in case one lies inside another.
                    var clearance = geometry.Cutouts.Min(c => c.ClosestPointTo(candidate).DistanceTo(candidate))
                        - FlattenAllowance;

                    if (clearance > item.HoleRadius)
                    {
                        item.HoleRadius = clearance;
                        item.HoleOffset = candidate - center;
                    }
                }
            }

            return item;
        }

        public override Item Clone()
        {
            var copy = new Item
            {
                Radius = this.Radius,
                Center = this.Center,
                Rotation = this.Rotation,
                PackingId = this.PackingId,
                FillRatio = this.FillRatio,
                PartOffset = this.PartOffset,
                HoleOffset = this.HoleOffset,
                HoleRadius = this.HoleRadius,
            };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
