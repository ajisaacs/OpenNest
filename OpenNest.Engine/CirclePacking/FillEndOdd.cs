using System;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CirclePacking
{
    internal class FillEndOdd : FillEngine
    {
        public FillEndOdd(Bin bin)
            : base(bin) { }

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

        private Bin FillHorizontal(Item item) => FillAxis(item, horizontal: true);

        private Bin FillVertical(Item item) => FillAxis(item, horizontal: false);

        private Bin FillAxis(Item item, bool horizontal)
        {
            var bin = Bin.Clone() as Bin;

            var max = new Vector(
                bin.Right - item.Radius + Tolerance.Epsilon,
                bin.Top - item.Radius + Tolerance.Epsilon
            );

            // A box's Length runs along X and its Width along Y.
            var primarySize = horizontal ? bin.Length : bin.Width;
            var count = System.Math.Floor((primarySize + Tolerance.Epsilon) / item.Diameter);

            if (count == 0)
                return bin;

            // A single circle per stripe cannot stagger; stack the stripes a diameter apart.
            var primaryOffset = count > 1
                ? (primarySize - item.Diameter) / (count - 1)
                : double.PositiveInfinity;

            // Adjacent stripes touch diagonally, but stripes two apart sit directly over each
            // other, so the stripe spacing can never drop below a radius.
            var secondaryOffset = count > 1
                ? System.Math.Max(Trigonometry.Height(primaryOffset * 0.5, item.Diameter), item.Diameter * 0.5)
                : item.Diameter;

            AddStripes(bin, item, new Stripes(
                InnerAlongX: horizontal,
                OuterStart: (horizontal ? bin.Y : bin.X) + item.Radius,
                OuterMax: horizontal ? max.Y : max.X,
                OuterStep: secondaryOffset,
                InnerStart: (horizontal ? bin.X : bin.Y) + item.Radius,
                InnerMax: horizontal ? max.X : max.Y,
                InnerStep: primaryOffset,
                Shift: count > 1 ? primaryOffset * 0.5 : 0));

            return bin;
        }
    }
}
