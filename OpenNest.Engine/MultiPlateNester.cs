using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    public enum PartClass
    {
        Large,
        Medium,
        Small,
    }

    public static class MultiPlateNester
    {
        public static List<NestItem> SortItems(List<NestItem> items, PartSortOrder sortOrder)
        {
            switch (sortOrder)
            {
                case PartSortOrder.BoundingBoxArea:
                    return items
                        .OrderByDescending(i =>
                        {
                            var bb = i.Drawing.Program.BoundingBox();
                            return bb.Width * bb.Length;
                        })
                        .ToList();

                case PartSortOrder.Size:
                    return items
                        .OrderByDescending(i =>
                        {
                            var bb = i.Drawing.Program.BoundingBox();
                            return System.Math.Max(bb.Width, bb.Length);
                        })
                        .ToList();

                default:
                    return items.ToList();
            }
        }

        public static PartClass Classify(Box partBounds, Box workArea)
        {
            var halfWidth = workArea.Width / 2.0;
            var halfLength = workArea.Length / 2.0;

            if (partBounds.Width > halfWidth || partBounds.Length > halfLength)
                return PartClass.Large;

            var workAreaArea = workArea.Width * workArea.Length;
            var partArea = partBounds.Width * partBounds.Length;

            if (partArea > workAreaArea / 9.0)
                return PartClass.Medium;

            return PartClass.Small;
        }
    }
}
