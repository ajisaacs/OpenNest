using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CirclePacking
{
    /// <summary>How far <see cref="StaggeredFill"/> shifts every other stripe along its length.</summary>
    internal enum StaggerMode
    {
        /// <summary>
        /// Half the pitch, so odd stripes nest in the gaps and hold one disc fewer; the pitch is
        /// stretched until a stripe's discs span the bin.
        /// </summary>
        HalfPitch,

        /// <summary>
        /// By the room a full stripe leaves, so every stripe holds as many discs as fit and odd
        /// stripes drop only as deep into the gaps as that shift allows.
        /// </summary>
        SlackShift,
    }

    /// <summary>
    /// Fills a bin with stripes of equal discs, every other stripe shifted along its length by the
    /// <see cref="StaggerMode"/>, stripes along X and along Y tried and the one placing more kept.
    /// </summary>
    internal static class StaggeredFill
    {
        public static void Fill(Bin bin, Item item, StaggerMode mode)
        {
            var alongX = Layout(bin, item, mode, alongX: true);
            var alongY = Layout(bin, item, mode, alongX: false);

            // On a tie, half-pitch stripes run along Y and slack-shifted ones along X.
            var keepAlongX = mode == StaggerMode.HalfPitch
                ? alongX.Count > alongY.Count
                : alongX.Count >= alongY.Count;

            bin.Items.AddRange(keepAlongX ? alongX : alongY);
        }

        /// <summary>Stripes run along X and step along Y when <paramref name="alongX"/>, and the reverse otherwise.</summary>
        private static List<Item> Layout(Bin bin, Item item, StaggerMode mode, bool alongX)
        {
            var items = new List<Item>();
            var diameter = item.Diameter;
            var radius = item.Radius;

            // A box's Length runs along X and its Width along Y.
            var stripeLength = alongX ? bin.Length : bin.Width;
            var count = System.Math.Floor((stripeLength + Tolerance.Epsilon) / diameter);

            if (count == 0)
                return items;

            var (pitch, step, shift) = mode == StaggerMode.HalfPitch
                ? HalfPitch(stripeLength, count, diameter)
                : SlackShift(stripeLength, count, diameter);

            var max = new Vector(
                bin.Right - radius + Tolerance.Epsilon,
                bin.Top - radius + Tolerance.Epsilon
            );

            AddStripes(items, item, new Stripes(
                AlongX: alongX,
                OuterStart: (alongX ? bin.Y : bin.X) + radius,
                OuterMax: alongX ? max.Y : max.X,
                OuterStep: step,
                InnerStart: (alongX ? bin.X : bin.Y) + radius,
                InnerMax: alongX ? max.X : max.Y,
                InnerStep: pitch,
                Shift: shift));

            return items;
        }

        private static (double Pitch, double Step, double Shift) HalfPitch(double stripeLength, double count, double diameter)
        {
            // A single disc per stripe cannot stagger; stack the stripes a diameter apart.
            if (count == 1)
                return (double.PositiveInfinity, diameter, 0);

            var pitch = (stripeLength - diameter) / (count - 1);

            // Adjacent stripes touch diagonally, but stripes two apart sit directly over each
            // other, so the stripe spacing can never drop below a radius.
            return (pitch, System.Math.Max(Trigonometry.Height(pitch * 0.5, diameter), diameter * 0.5), pitch * 0.5);
        }

        private static (double Pitch, double Step, double Shift) SlackShift(double stripeLength, double count, double diameter)
        {
            var remaining = stripeLength - diameter * count;

            if (remaining < diameter * 0.5)
                return (diameter, Trigonometry.Base(remaining, diameter), remaining);

            var shift = (stripeLength - diameter) / (2 * count - 1);

            // Adjacent stripes touch diagonally, but stripes two apart sit directly beside
            // each other, so the stripe spacing can never drop below a radius.
            return (shift * 2.0, System.Math.Max(Trigonometry.Base(shift, diameter), diameter * 0.5), shift);
        }

        /// <summary>
        /// Lays staggered stripes <see cref="Stripes.OuterStep"/> apart, odd stripes shifted along
        /// their length by <see cref="Stripes.Shift"/>. The last stripes may instead stack
        /// straight: a diameter apart, each repeating the last staggered stripe's positions.
        /// Straight stacking climbs faster per stripe than staggering, so where the bin leaves
        /// too little for one more staggered stripe, a few straight ones can fit more. Every
        /// switch point is tried, all staggered and all straight included, and the one placing
        /// the most is kept; a tie keeps the stripes staggered.
        /// </summary>
        private static void AddStripes(List<Item> items, Item item, Stripes stripes)
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
                AddStripe(items, item, stripes, staggered[stripe], InnerStart(stripes, stripe));

            var last = switchAt - 1;
            var straightOuter = last >= 0 ? staggered[last] : 0;
            for (var stripe = 0; stripe < straightCount; stripe++)
            {
                straightOuter += item.Diameter;
                AddStripe(items, item, stripes, straightOuter, InnerStart(stripes, last));
            }
        }

        private static double InnerStart(Stripes stripes, int stripe) =>
            stripe.IsOdd() ? stripes.InnerStart + stripes.Shift : stripes.InnerStart;

        private static void AddStripe(List<Item> items, Item item, Stripes stripes, double outer, double innerStart)
        {
            for (var inner = innerStart; inner <= stripes.InnerMax; inner += stripes.InnerStep)
            {
                var addedItem = item.Clone();
                addedItem.Center = stripes.AlongX
                    ? new Vector(inner, outer)
                    : new Vector(outer, inner);
                items.Add(addedItem);
            }
        }

        /// <summary>
        /// Stripes run along the inner axis, X when <see cref="AlongX"/>, and step along the
        /// other; starts and maxima are disc centers.
        /// </summary>
        private readonly record struct Stripes(
            bool AlongX,
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
