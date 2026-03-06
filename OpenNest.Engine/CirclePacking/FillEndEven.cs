using System;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CirclePacking
{
    internal class FillEndEven : FillEngine
    {
        public FillEndEven(Bin bin)
            : base(bin)
        {
        }

        public override void Fill(Item item)
        {
            var max = new Vector(
                Bin.Right - item.BoundingBox.Right + Tolerance.Epsilon,
                Bin.Top - item.BoundingBox.Top + Tolerance.Epsilon);

            var rows = System.Math.Floor((Bin.Height + Tolerance.Epsilon) / (item.Diameter));

            var diameter = item.Diameter;
            var remaining = Bin.Height - diameter * rows;
            var radius = diameter * 0.5;

            if (remaining < radius)
            {
                var yodd = Bin.Y + remaining;
                var yoffset = diameter;
                var xoffset = Trigonometry.Base(remaining, diameter);
                int column = 0;

                for (var x = Bin.X; x <= max.X; x += xoffset)
                {
                    var y = column.IsOdd() ? yodd : Bin.Y;

                    for (; y <= max.Y; y += yoffset)
                    {
                        Bin.Items.Add(new Item
                        {
                            Center = new Vector(x, y)
                        });
                    }

                    column++;
                }
            }
            else
            {
                var yoffset = (Bin.Height - diameter) / (2 * rows - 1);
                var xoffset = Trigonometry.Base(yoffset, diameter);

                var yodd = Bin.Y + yoffset;

                yoffset *= 2.0;

                int column = 0;

                for (var x = Bin.X; x <= max.X; x += xoffset)
                {
                    var y = column.IsOdd() ? yodd : Bin.Y;

                    for (; y <= max.Y; y += yoffset)
                    {
                        Bin.Items.Add(new Item
                        {
                            Center = new Vector(x, y)
                        });
                    }

                    column++;
                }
            }
        }

        public override void Fill(Item item, int maxCount)
        {
            throw new NotImplementedException();
        }
    }
}
