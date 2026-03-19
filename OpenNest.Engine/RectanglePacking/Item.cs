using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.RectanglePacking
{
    internal class Item : Box
    {
        public int Id { get; set; }

        public bool IsRotated { get; private set; }

        public void Rotate()
        {
            Generic.Swap(ref Size.Width, ref Size.Length);
            IsRotated = !IsRotated;
        }

        public object Clone()
        {
            return new Item
            {
                IsRotated = this.IsRotated,
                Location = this.Location,
                Size = this.Size,
                Id = this.Id
            };
        }
    }

    internal static class ItemListExtensions
    {
        public static Box GetBoundingBox(this IList<Item> items)
        {
            if (items.Count == 0)
                return Box.Empty;

            double minX = items[0].X;
            double minY = items[0].Y;
            double maxX = items[0].X + items[0].Width;
            double maxY = items[0].Y + items[0].Length;

            foreach (var box in items)
            {
                if (box.Left < minX) minX = box.Left;
                if (box.Right > maxX) maxX = box.Right;
                if (box.Bottom < minY) minY = box.Bottom;
                if (box.Top > maxY) maxY = box.Top;
            }

            return new Box(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
