using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Dimensions
{
    /// <summary>
    /// Resolves dimensions against the entities being cut (visible, with roles, breakoffs and
    /// tabbed cutouts applied), so a dimension measures the part as it will come off the table.
    /// </summary>
    public static class DimensionBuilder
    {
        private const double DefaultOffsetFactor = 0.1;

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
    }
}
