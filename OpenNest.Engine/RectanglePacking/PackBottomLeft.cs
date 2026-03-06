using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.RectanglePacking
{
    internal class PackBottomLeft : PackEngine
    {
        private List<Vector> points;

        public PackBottomLeft(Bin bin)
            : base(bin)
        {
            points = new List<Vector>();
        }

        public override void Pack(List<Item> items)
        {
            items = items.OrderBy(i => -i.Area()).ToList();

            points.Add(Bin.Location);

            var skip = new List<int>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (skip.Contains(item.Id))
                    continue;

                var pt = FindPointVertical(item);

                if (pt == null)
                {
                    skip.Add(item.Id);
                    continue;
                }

                item.Location = pt.Value;

                points.Remove(pt.Value);
                points.Add(new Vector(item.Left, item.Top));
                points.Add(new Vector(item.Right, item.Bottom));

                Bin.Items.Add(item);
            }

            points.Clear();
        }

        private Vector? FindPointVertical(Item item)
        {
            var pt = new Vector(double.MaxValue, double.MaxValue);

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];

                item.Location = point;

                if (!IsValid(item))
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

        private Vector? FindPointHorizontal(Item item)
        {
            var pt = new Vector(double.MaxValue, double.MaxValue);

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];

                item.Location = point;

                if (!IsValid(item))
                    continue;

                if (point.Y < pt.Y)
                    pt = point;
                else if (point.Y.IsEqualTo(pt.Y) && point.X < pt.X)
                    pt = point;
            }

            if (pt.X != double.MaxValue && pt.Y != double.MaxValue)
                return pt;

            return null;
        }

        private bool IsValid(Item item)
        {
            if (!Bin.Contains(item))
                return false;

            foreach (var it in Bin.Items)
            {
                if (item.Intersects(it))
                    return false;
            }

            return true;
        }
    }
}
