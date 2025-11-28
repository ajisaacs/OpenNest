using System.Collections.Generic;
using System.Linq;

namespace OpenNest
{
    public static class BoundingBox
    {
        public static Box GetBoundingBox(this IList<Box> boxes)
        {
            if (boxes.Count == 0)
                return Box.Empty;

            double minX = boxes[0].X;
            double minY = boxes[0].Y;
            double maxX = boxes[0].X + boxes[0].Width;
            double maxY = boxes[0].Y + boxes[0].Height;

            foreach (var box in boxes)
            {
                if (box.Left < minX) minX = box.Left;
                if (box.Right > maxX) maxX = box.Right;
                if (box.Bottom < minY) minY = box.Bottom;
                if (box.Top > maxY) maxY = box.Top;
            }

            return new Box(minX, minY, maxX - minX, maxY - minY);
        }

        public static Box GetBoundingBox(this IList<Vector> pts)
        {
            if (pts.Count == 0)
                return Box.Empty;

            var first = pts[0];
            var minX = first.X;
            var maxX = first.X;
            var minY = first.Y;
            var maxY = first.Y;

            for (int i = 1; i < pts.Count; ++i)
            {
                var vertex = pts[i];

                if (vertex.X < minX) minX = vertex.X;
                else if (vertex.X > maxX) maxX = vertex.X;

                if (vertex.Y < minY) minY = vertex.Y;
                else if (vertex.Y > maxY) maxY = vertex.Y;
            }

            return new Box(minX, minY, maxX - minX, maxY - minY);
        }

        public static Box GetBoundingBox(this IEnumerable<IBoundable> items)
        {
            var first = items.FirstOrDefault();

            if (first == null)
                return Box.Empty;

            double left = first.Left;
            double bottom = first.Bottom;
            double right = first.Right;
            double top = first.Top;

            foreach (var box in items)
            {
                if (box.Left < left) left = box.Left;
                if (box.Right > right) right = box.Right;
                if (box.Bottom < bottom) bottom = box.Bottom;
                if (box.Top > top) top = box.Top;
            }

            return new Box(left, bottom, right - left, top - bottom);
        }
    }
}
