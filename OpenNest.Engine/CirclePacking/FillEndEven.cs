using System;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CirclePacking
{
    internal class FillEndEven : FillEngine
    {
        public FillEndEven(Bin bin)
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

        /// <summary>Stripes run along Y and step along X when horizontal, and the reverse otherwise.</summary>
        private Bin FillAxis(Item item, bool horizontal)
        {
            var bin = Bin.Clone() as Bin;

            var max = new Vector(
                bin.Right - item.Radius + Tolerance.Epsilon,
                bin.Top - item.Radius + Tolerance.Epsilon
            );

            // A box's Length runs along X and its Width along Y.
            var stripeSize = horizontal ? bin.Width : bin.Length;
            var diameter = item.Diameter;
            var radius = item.Radius;
            var rows = System.Math.Floor((stripeSize + Tolerance.Epsilon) / diameter);

            if (rows == 0)
                return bin;

            var remaining = stripeSize - diameter * rows;
            double oddShift;
            double innerOffset;
            double outerOffset;

            if (remaining < radius)
            {
                oddShift = remaining;
                innerOffset = diameter;
                outerOffset = Trigonometry.Base(remaining, diameter);
            }
            else
            {
                oddShift = (stripeSize - diameter) / (2 * rows - 1);
                innerOffset = oddShift * 2.0;
                // Adjacent stripes touch diagonally, but stripes two apart sit directly beside
                // each other, so the stripe spacing can never drop below a radius.
                outerOffset = System.Math.Max(Trigonometry.Base(oddShift, diameter), radius);
            }

            var outerStart = (horizontal ? bin.X : bin.Y) + radius;
            var outerMax = horizontal ? max.X : max.Y;
            var innerStart = (horizontal ? bin.Y : bin.X) + radius;
            var innerMax = horizontal ? max.Y : max.X;

            var stripe = 0;

            for (var outer = outerStart; outer <= outerMax; outer += outerOffset)
            {
                var inner = stripe.IsOdd() ? innerStart + oddShift : innerStart;

                for (; inner <= innerMax; inner += innerOffset)
                {
                    var addedItem = item.Clone();
                    addedItem.Center = horizontal
                        ? new Vector(outer, inner)
                        : new Vector(inner, outer);
                    bin.Items.Add(addedItem);
                }

                stripe++;
            }

            return bin;
        }
    }
}
