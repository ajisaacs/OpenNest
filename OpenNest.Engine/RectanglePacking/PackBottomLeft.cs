using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.RectanglePacking
{
    internal class PackBottomLeft : PackEngine
    {
        public PackBottomLeft(Bin bin)
            : base(bin)
        {
        }

        public override void Pack(List<Item> items)
        {
            var byArea = items.Select(i => i.Clone() as Item).OrderByDescending(i => i.Area()).ToList();
            var byLength = items.Select(i => i.Clone() as Item).OrderByDescending(i => System.Math.Max(i.Width, i.Length)).ToList();

            var resultA = PackWithOrder(byArea);
            var resultB = PackWithOrder(byLength);

            var winner = PickWinner(resultA, resultB);

            Bin.Items.AddRange(winner);
        }

        private List<Item> PackWithOrder(List<Item> items)
        {
            var points = new List<Vector> { Bin.Location };
            var placed = new List<Item>();
            var skip = new List<int>();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (skip.Contains(item.Id))
                    continue;

                var pt = FindPointVertical(item, points, placed);

                // If it doesn't fit, try rotated.
                if (pt == null)
                {
                    item.Rotate();
                    pt = FindPointVertical(item, points, placed);
                }

                if (pt == null)
                {
                    if (item.IsRotated)
                        item.Rotate();
                    skip.Add(item.Id);
                    continue;
                }

                item.Location = pt.Value;

                points.Remove(pt.Value);
                points.Add(new Vector(item.Left, item.Top));
                points.Add(new Vector(item.Right, item.Bottom));

                placed.Add(item);
            }

            return placed;
        }

        private static List<Item> PickWinner(List<Item> a, List<Item> b)
        {
            if (a.Count != b.Count)
                return a.Count > b.Count ? a : b;

            if (a.Count == 0)
                return a;

            var areaA = a.GetBoundingBox().Area();
            var areaB = b.GetBoundingBox().Area();

            return areaB < areaA ? b : a;
        }

        private Vector? FindPointVertical(Item item, List<Vector> points, List<Item> placed)
        {
            var pt = new Vector(double.MaxValue, double.MaxValue);

            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];

                item.Location = point;

                if (!IsValid(item, placed))
                    continue;

                if (point.X < pt.X)
                    pt = point;
                else if (point.X.IsEqualTo(pt.X) && point.Y < pt.Y)
                    pt = point;
            }

            if (pt.X != double.MaxValue && pt.Y != double.MaxValue)
                return pt;

            return null;
        }

        private bool IsValid(Item item, List<Item> placed)
        {
            if (!Bin.Contains(item))
                return false;

            foreach (var it in placed)
            {
                if (item.Intersects(it))
                    return false;
            }

            return true;
        }
    }
}
