using System;
using OpenNest.Math;

namespace OpenNest.RectanglePacking
{
    internal class FillNoRotation : FillEngine
    {
        public FillNoRotation(Bin bin)
            : base(bin)
        {
        }

        public NestDirection NestDirection { get; set; }

        public override void Fill(Item item)
        {
            var ycount = (int)System.Math.Floor((Bin.Height + Tolerance.Epsilon) / item.Height);
            var xcount = (int)System.Math.Floor((Bin.Width + Tolerance.Epsilon) / item.Width);
            var count = ycount * xcount;

            for (int i = 0; i < xcount; i++)
            {
                var x = item.Width * i + Bin.X;

                for (int j = 0; j < ycount; j++)
                {
                    var y = item.Height * j + Bin.Y;

                    var addedItem = item.Clone() as Item;
                    addedItem.Location = new Vector(x, y);

                    Bin.Items.Add(addedItem);
                }
            }
        }

        public override void Fill(Item item, int maxCount)
        {
            var ycount = (int)System.Math.Floor((Bin.Height + Tolerance.Epsilon) / item.Height);
            var xcount = (int)System.Math.Floor((Bin.Width + Tolerance.Epsilon) / item.Width);
            var count = ycount * xcount;

            if (count <= maxCount)
            {
                Fill(item);
                return;
            }

            var columns = 0;
            var rows = 0;

            if (NestDirection == NestDirection.Vertical)
            {
                columns = (int)System.Math.Ceiling((double)maxCount / ycount);
                rows = (int)System.Math.Ceiling((double)maxCount / columns);
            }
            else
            {
                rows = (int)System.Math.Ceiling((double)maxCount / xcount);
                columns = (int)System.Math.Ceiling((double)maxCount / rows);
            }

            if (item.Width > item.Height)
                VPattern(item, rows, columns, maxCount);
            else
                HPattern(item, rows, columns, maxCount);
        }
    }
}
