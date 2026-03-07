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
    }
}
