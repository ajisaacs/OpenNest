using System;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CirclePacking
{
    internal class FillEndOdd : FillEngine
    {
        public FillEndOdd(Bin bin)
            : base(bin)
        {
        }

        public override void Fill(Item item)
        {
            var bin1 = FillHorizontal(item);
            var bin2 = FillVertical(item);

            if (bin1.Items.Count > bin2.Items.Count)
                Bin.Items.AddRange(bin1.Items);
            else
                Bin.Items.AddRange(bin2.Items);
        }

        public override void Fill(Item item, int maxCount)
        {
            throw new NotImplementedException();
        }

        private Bin FillHorizontal(Item item)
        {
            var bin = Bin.Clone() as Bin;

            var max = new Vector(
                bin.Right - item.BoundingBox.Right + Tolerance.Epsilon,
                bin.Top - item.BoundingBox.Top + Tolerance.Epsilon);

            var count = System.Math.Floor((bin.Width + Tolerance.Epsilon) / item.Diameter);

            if (count == 0)
                return bin;

            var xoffset = (bin.Width - item.Diameter) / (count - 1);
            var yoffset = Trigonometry.Height(xoffset * 0.5, item.Diameter);

            int row = 0;

            for (var y = bin.Y; y <= max.Y; y += yoffset)
            {
                var x = row.IsOdd() ? bin.X + xoffset * 0.5 : bin.X;

                for (; x <= max.X; x += xoffset)
                {
                    var addedItem = item.Clone() as Item;
                    addedItem.Center = new Vector(x, y);

                    bin.Items.Add(addedItem);
                }

                row++;
            }

            return bin;
        }

        private Bin FillVertical(Item item)
        {
            var bin = Bin.Clone() as Bin;

            var max = new Vector(
                Bin.Right - item.BoundingBox.Right + Tolerance.Epsilon,
                Bin.Top - item.BoundingBox.Top + Tolerance.Epsilon);

            var count = System.Math.Floor((bin.Height + Tolerance.Epsilon) / item.Diameter);

            if (count == 0)
                return bin;

            var yoffset = (bin.Height - item.Diameter) / (count - 1);
            var xoffset = Trigonometry.Base(yoffset * 0.5, item.Diameter);

            int column = 0;

            for (var x = bin.X; x <= max.X; x += xoffset)
            {
                var y = column.IsOdd() ? bin.Y + yoffset * 0.5 : bin.Y;

                for (; y <= max.Y; y += yoffset)
                {
                    var addedItem = item.Clone() as Item;
                    addedItem.Center = new Vector(x, y);

                    bin.Items.Add(addedItem);
                }

                column++;
            }

            return bin;
        }
    }
}
