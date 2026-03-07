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
    }
}
