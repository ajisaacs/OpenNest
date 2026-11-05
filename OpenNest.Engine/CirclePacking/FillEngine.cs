using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CirclePacking
{
    internal abstract class FillEngine
    {
        public FillEngine(Bin bin)
        {
            Bin = bin;
        }

        public Bin Bin { get; set; }

        public abstract void Fill(Item item);

        public abstract void Fill(Item item, int maxCount);

        /// <summary>
        /// Lays staggered stripes <see cref="Stripes.OuterStep"/> apart into <paramref name="bin"/>,
        /// odd stripes shifted along their length by <see cref="Stripes.Shift"/>. The last stripes
        /// may instead stack straight: a diameter apart, each repeating the last staggered stripe's
        /// positions. Straight stacking climbs faster per stripe than staggering, so where the
        /// sheet leaves too little for one more staggered stripe, a few straight ones can fit more.
        /// Every switch point is tried, all staggered and all straight included, and the one placing
        /// the most is kept; a tie keeps the stripes staggered.
        /// </summary>
        protected static void AddStripes(Bin bin, Item item, Stripes stripes)
        {
            var staggered = new List<double>();
            for (var outer = stripes.OuterStart; outer <= stripes.OuterMax; outer += stripes.OuterStep)
                staggered.Add(outer);

            var counts = new int[staggered.Count];
            var total = 0;
            for (var stripe = 0; stripe < counts.Length; stripe++)
            {
                for (var inner = InnerStart(stripes, stripe); inner <= stripes.InnerMax; inner += stripes.InnerStep)
                    counts[stripe]++;
                total += counts[stripe];
            }

            var switchAt = staggered.Count;
            var straightCount = 0;
            var best = total;
            var placed = 0;

            for (var stripe = 0; stripe < counts.Length; stripe++)
            {
                placed += counts[stripe];
                var straight = 0;
                for (var outer = staggered[stripe] + item.Diameter; outer <= stripes.OuterMax; outer += item.Diameter)
                    straight++;

                if (placed + straight * counts[stripe] > best)
                {
                    best = placed + straight * counts[stripe];
                    switchAt = stripe + 1;
                    straightCount = straight;
                }
            }

            for (var stripe = 0; stripe < switchAt; stripe++)
                AddStripe(bin, item, stripes, staggered[stripe], InnerStart(stripes, stripe));

            var last = switchAt - 1;
            var straightOuter = last >= 0 ? staggered[last] : 0;
            for (var stripe = 0; stripe < straightCount; stripe++)
            {
                straightOuter += item.Diameter;
                AddStripe(bin, item, stripes, straightOuter, InnerStart(stripes, last));
            }
        }

        private static double InnerStart(Stripes stripes, int stripe) =>
            stripe.IsOdd() ? stripes.InnerStart + stripes.Shift : stripes.InnerStart;

        private static void AddStripe(Bin bin, Item item, Stripes stripes, double outer, double innerStart)
        {
            for (var inner = innerStart; inner <= stripes.InnerMax; inner += stripes.InnerStep)
            {
                var addedItem = item.Clone();
                addedItem.Center = stripes.InnerAlongX
                    ? new Vector(inner, outer)
                    : new Vector(outer, inner);
                bin.Items.Add(addedItem);
            }
        }

        /// <summary>
        /// Stripes run along the inner axis, X when <see cref="InnerAlongX"/>, and step along the
        /// other; starts and maxima are circle centers.
        /// </summary>
        protected readonly record struct Stripes(
            bool InnerAlongX,
            double OuterStart,
            double OuterMax,
            double OuterStep,
            double InnerStart,
            double InnerMax,
            double InnerStep,
            double Shift
        );
    }
}
