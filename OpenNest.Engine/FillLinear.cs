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

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical parts along an axis.
        /// Places part B at bounding box offset from part A, then pushes B back toward A
        /// using directional distance to find the tightest non-overlapping position.
        /// </summary>
        private double FindCopyDistance(Part partA, NestDirection direction)
        {
            var bbox = partA.BoundingBox;
            double bboxDim;
            PushDirection pushDir;
            Vector copyOffset;

            if (direction == NestDirection.Horizontal)
            {
                bboxDim = bbox.Width;
                pushDir = PushDirection.Left;
                copyOffset = new Vector(bboxDim, 0);
            }
            else
            {
                bboxDim = bbox.Height;
                pushDir = PushDirection.Down;
                copyOffset = new Vector(0, bboxDim);
            }

            // Create part B offset by bounding box dimension (guaranteed no overlap).
            var partB = (Part)partA.Clone();
            partB.Offset(copyOffset);

            // Get geometry lines for push calculation.
            var opposite = Helper.OppositeDirection(pushDir);

            var movingLines = PartSpacing > 0
                ? Helper.GetOffsetPartLines(partB, PartSpacing, pushDir)
                : Helper.GetPartLines(partB, pushDir);

            var stationaryLines = Helper.GetPartLines(partA, opposite);

            // Find how far B can slide toward A.
            var slideDistance = Helper.DirectionalDistance(movingLines, stationaryLines, pushDir);

            if (slideDistance >= double.MaxValue || slideDistance < 0)
                return bboxDim;

            return bboxDim - slideDistance;
        }

        /// <summary>
        /// Fills a single row of identical parts along one axis using geometry-aware spacing.
        /// Returns a Pattern containing all placed parts.
        /// </summary>
        public Pattern FillRow(Drawing drawing, double rotationAngle, NestDirection direction)
        {
            var pattern = new Pattern();

            // Create the template part with rotation applied.
            var template = new Part(drawing);

            if (!rotationAngle.IsEqualTo(0))
                template.Rotate(rotationAngle);

            // Position template at work area origin.
            var bbox = template.Program.BoundingBox();
            template.Offset(WorkArea.Location - bbox.Location);
            template.UpdateBounds();

            // Check if the part fits in the work area at all.
            if (template.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon ||
                template.BoundingBox.Height > WorkArea.Height + Tolerance.Epsilon)
                return pattern;

            pattern.Parts.Add(template);

            // Find the geometry-aware copy distance.
            var copyDistance = FindCopyDistance(template, direction);

            if (copyDistance <= 0)
            {
                pattern.UpdateBounds();
                return pattern;
            }

            // Fill the row by copying at the fixed interval.
            double limit = direction == NestDirection.Horizontal
                ? WorkArea.Right
                : WorkArea.Top;

            double partDim = direction == NestDirection.Horizontal
                ? template.BoundingBox.Width
                : template.BoundingBox.Height;

            int count = 1;

            while (true)
            {
                double nextPos = (direction == NestDirection.Horizontal
                    ? template.BoundingBox.Left
                    : template.BoundingBox.Bottom) + copyDistance * count;

                // Check if the next part would exceed the work area.
                if (nextPos + partDim > limit + Tolerance.Epsilon)
                    break;

                var offset = direction == NestDirection.Horizontal
                    ? new Vector(copyDistance * count, 0)
                    : new Vector(0, copyDistance * count);

                var clone = (Part)template.Clone();
                clone.Offset(offset);
                pattern.Parts.Add(clone);

                count++;
            }

            pattern.UpdateBounds();
            return pattern;
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical patterns along an axis.
        /// Same push algorithm as FindCopyDistance but operates on pattern line groups.
        /// </summary>
        private double FindPatternCopyDistance(Pattern patternA, NestDirection direction)
        {
            var bbox = patternA.BoundingBox;
            double bboxDim;
            PushDirection pushDir;
            Vector copyOffset;

            if (direction == NestDirection.Horizontal)
            {
                bboxDim = bbox.Width;
                pushDir = PushDirection.Left;
                copyOffset = new Vector(bboxDim, 0);
            }
            else
            {
                bboxDim = bbox.Height;
                pushDir = PushDirection.Down;
                copyOffset = new Vector(0, bboxDim);
            }

            var patternB = patternA.Clone(copyOffset);

            var opposite = Helper.OppositeDirection(pushDir);

            var movingLines = PartSpacing > 0
                ? patternB.GetOffsetLines(PartSpacing, pushDir)
                : patternB.GetLines(pushDir);

            var stationaryLines = patternA.GetLines(opposite);

            var slideDistance = Helper.DirectionalDistance(movingLines, stationaryLines, pushDir);

            if (slideDistance >= double.MaxValue || slideDistance < 0)
                return bboxDim;

            return bboxDim - slideDistance;
        }

        /// <summary>
        /// Fills the work area by creating a row along the primary axis,
        /// then tiling that row pattern along the perpendicular axis.
        /// </summary>
        public List<Part> Fill(Drawing drawing, double rotationAngle, NestDirection primaryAxis)
        {
            var result = new List<Part>();

            // Step 1: Build the row pattern along the primary axis.
            var rowPattern = FillRow(drawing, rotationAngle, primaryAxis);

            if (rowPattern.Parts.Count == 0)
                return result;

            // Add the first row.
            result.AddRange(rowPattern.Parts);

            // Step 2: Tile the row pattern along the perpendicular axis.
            var perpAxis = primaryAxis == NestDirection.Horizontal
                ? NestDirection.Vertical
                : NestDirection.Horizontal;

            var copyDistance = FindPatternCopyDistance(rowPattern, perpAxis);

            if (copyDistance <= 0)
                return result;

            double limit = perpAxis == NestDirection.Horizontal
                ? WorkArea.Right
                : WorkArea.Top;

            double patternDim = perpAxis == NestDirection.Horizontal
                ? rowPattern.BoundingBox.Width
                : rowPattern.BoundingBox.Height;

            int count = 1;

            while (true)
            {
                double nextPos = (perpAxis == NestDirection.Horizontal
                    ? rowPattern.BoundingBox.Left
                    : rowPattern.BoundingBox.Bottom) + copyDistance * count;

                if (nextPos + patternDim > limit + Tolerance.Epsilon)
                    break;

                var offset = perpAxis == NestDirection.Horizontal
                    ? new Vector(copyDistance * count, 0)
                    : new Vector(0, copyDistance * count);

                var clone = rowPattern.Clone(offset);
                result.AddRange(clone.Parts);

                count++;
            }

            return result;
        }
    }
}
