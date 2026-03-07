using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public class FillLinear
    {
        public FillLinear(Box workArea, double partSpacing)
        {
            WorkArea = workArea;
            PartSpacing = partSpacing;
        }

        public Box WorkArea { get; }

        public double PartSpacing { get; }

        private static Vector MakeOffset(NestDirection direction, double distance)
        {
            return direction == NestDirection.Horizontal
                ? new Vector(distance, 0)
                : new Vector(0, distance);
        }

        private static PushDirection GetPushDirection(NestDirection direction)
        {
            return direction == NestDirection.Horizontal
                ? PushDirection.Left
                : PushDirection.Down;
        }

        private static double GetDimension(Box box, NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? box.Width : box.Height;
        }

        private static double GetStart(Box box, NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? box.Left : box.Bottom;
        }

        private double GetLimit(NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? WorkArea.Right : WorkArea.Top;
        }

        private static NestDirection PerpendicularAxis(NestDirection direction)
        {
            return direction == NestDirection.Horizontal
                ? NestDirection.Vertical
                : NestDirection.Horizontal;
        }

        /// <summary>
        /// Computes the slide distance for the push algorithm, returning the
        /// geometry-aware copy distance along the given axis.
        /// </summary>
        private double ComputeCopyDistance(double bboxDim, double slideDistance)
        {
            if (slideDistance >= double.MaxValue || slideDistance < 0)
                return bboxDim + PartSpacing;

            return bboxDim - slideDistance;
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical parts along an axis.
        /// </summary>
        private double FindCopyDistance(Part partA, NestDirection direction)
        {
            var bboxDim = GetDimension(partA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);

            var partB = (Part)partA.Clone();
            partB.Offset(MakeOffset(direction, bboxDim));

            var opposite = Helper.OppositeDirection(pushDir);
            var movingLines = Helper.GetOffsetPartLines(partB, PartSpacing, pushDir);
            var stationaryLines = Helper.GetPartLines(partA, opposite);
            var slideDistance = Helper.DirectionalDistance(movingLines, stationaryLines, pushDir);

            return ComputeCopyDistance(bboxDim, slideDistance);
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical patterns along an axis.
        /// </summary>
        private double FindPatternCopyDistance(Pattern patternA, NestDirection direction)
        {
            var bboxDim = GetDimension(patternA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);

            var patternB = patternA.Clone(MakeOffset(direction, bboxDim));

            var opposite = Helper.OppositeDirection(pushDir);
            var movingLines = patternB.GetOffsetLines(PartSpacing, pushDir);
            var stationaryLines = patternA.GetLines(opposite);
            var slideDistance = Helper.DirectionalDistance(movingLines, stationaryLines, pushDir);

            return ComputeCopyDistance(bboxDim, slideDistance);
        }

        /// <summary>
        /// Tiles a pattern along the given axis, returning the cloned parts
        /// (does not include the original pattern's parts).
        /// </summary>
        private List<Part> TilePattern(Pattern basePattern, NestDirection direction)
        {
            var result = new List<Part>();
            var copyDistance = FindPatternCopyDistance(basePattern, direction);

            if (copyDistance <= 0)
                return result;

            var dim = GetDimension(basePattern.BoundingBox, direction);
            var start = GetStart(basePattern.BoundingBox, direction);
            var limit = GetLimit(direction);

            var count = 1;

            while (true)
            {
                var nextPos = start + copyDistance * count;

                if (nextPos + dim > limit + Tolerance.Epsilon)
                    break;

                var clone = basePattern.Clone(MakeOffset(direction, copyDistance * count));
                result.AddRange(clone.Parts);
                count++;
            }

            return result;
        }

        /// <summary>
        /// Fills a single row of identical parts along one axis using geometry-aware spacing.
        /// </summary>
        public Pattern FillRow(Drawing drawing, double rotationAngle, NestDirection direction)
        {
            var pattern = new Pattern();

            var template = new Part(drawing);

            if (!rotationAngle.IsEqualTo(0))
                template.Rotate(rotationAngle);

            var bbox = template.Program.BoundingBox();
            template.Offset(WorkArea.Location - bbox.Location);
            template.UpdateBounds();

            if (template.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon ||
                template.BoundingBox.Height > WorkArea.Height + Tolerance.Epsilon)
                return pattern;

            pattern.Parts.Add(template);

            var copyDistance = FindCopyDistance(template, direction);

            if (copyDistance <= 0)
            {
                pattern.UpdateBounds();
                return pattern;
            }

            var dim = GetDimension(template.BoundingBox, direction);
            var start = GetStart(template.BoundingBox, direction);
            var limit = GetLimit(direction);

            var count = 1;

            while (true)
            {
                var nextPos = start + copyDistance * count;

                if (nextPos + dim > limit + Tolerance.Epsilon)
                    break;

                var clone = (Part)template.Clone();
                clone.Offset(MakeOffset(direction, copyDistance * count));
                pattern.Parts.Add(clone);
                count++;
            }

            pattern.UpdateBounds();
            return pattern;
        }

        /// <summary>
        /// Fills the work area by tiling a pre-built pattern along both axes.
        /// </summary>
        public List<Part> Fill(Pattern pattern, NestDirection primaryAxis)
        {
            var result = new List<Part>();

            if (pattern.Parts.Count == 0)
                return result;

            var offset = WorkArea.Location - pattern.BoundingBox.Location;
            var basePattern = pattern.Clone(offset);

            if (basePattern.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon ||
                basePattern.BoundingBox.Height > WorkArea.Height + Tolerance.Epsilon)
                return result;

            result.AddRange(basePattern.Parts);

            // Tile along the primary axis.
            var primaryTiles = TilePattern(basePattern, primaryAxis);
            result.AddRange(primaryTiles);

            // Build a full-row pattern for perpendicular tiling.
            if (primaryTiles.Count > 0)
            {
                var rowPattern = new Pattern();
                rowPattern.Parts.AddRange(result);
                rowPattern.UpdateBounds();
                basePattern = rowPattern;
            }

            // Tile along the perpendicular axis.
            result.AddRange(TilePattern(basePattern, PerpendicularAxis(primaryAxis)));

            return result;
        }

        /// <summary>
        /// Fills the work area by creating a row along the primary axis,
        /// then tiling that row pattern along the perpendicular axis.
        /// </summary>
        public List<Part> Fill(Drawing drawing, double rotationAngle, NestDirection primaryAxis)
        {
            var rowPattern = FillRow(drawing, rotationAngle, primaryAxis);

            if (rowPattern.Parts.Count == 0)
                return new List<Part>();

            var result = new List<Part>(rowPattern.Parts);
            result.AddRange(TilePattern(rowPattern, PerpendicularAxis(primaryAxis)));

            return result;
        }
    }
}
