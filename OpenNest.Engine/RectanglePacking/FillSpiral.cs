using System.Linq;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.RectanglePacking
{
    internal class FillSpiral : FillEngine
    {
        public Box CenterRemnant { get; private set; }

        /// <summary>
        /// A spiral other than the least-waste one must hold more parts than this, counting a
        /// plain grid in its center. Set to the count already in hand, so a pinwheel replaces a
        /// tidier layout only when it adds parts.
        /// </summary>
        public int AlternativeMustExceed { get; init; }

        public FillSpiral(Bin bin)
            : base(bin) { }

        public override void Fill(Item item)
        {
            Fill(item, int.MaxValue);
        }

        public override void Fill(Item item, int maxCount)
        {
            if (item == null)
                return;

            // Width = Y axis, Length = X axis
            var (comboY, comboX) = SelectCombinations(item);

            if (comboY == null)
                return; // cant do an efficient spiral fill

            var q14size = Q14Size(item, comboY, comboX);
            var q23size = Q23Size(item, comboY, comboX);

            // Q1: normal orientation at bin origin
            item.Location = Bin.Location;
            var q1 = FillGrid(item, comboY.Count1, comboX.Count1, maxCount);
            Bin.Items.AddRange(q1);

            // Q2: rotated, above Q1
            item.Rotate();
            item.Location = new Vector(Bin.X, Bin.Y + q14size.Width);
            var q2 = FillGrid(item, comboY.Count2, comboX.Count2, maxCount - Bin.Items.Count);
            Bin.Items.AddRange(q2);

            // Q3: rotated, right of Q1
            item.Location = new Vector(Bin.X + q14size.Length, Bin.Y);
            var q3 = FillGrid(item, comboY.Count2, comboX.Count2, maxCount - Bin.Items.Count);
            Bin.Items.AddRange(q3);

            // Q4: normal orientation, diagonal from Q1
            item.Rotate();
            item.Location = new Vector(Bin.X + q23size.Length, Bin.Y + q23size.Width);
            var q4 = FillGrid(item, comboY.Count1, comboX.Count1, maxCount);
            Bin.Items.AddRange(q4);

            // Compute center remnant — the rectangular gap between the 4 quadrants
            // Only valid when all 4 quadrants have items; otherwise the "center"
            // overlaps an occupied quadrant and recursion never terminates.
            var centerW = System.Math.Abs(q14size.Length - q23size.Length);
            var centerH = System.Math.Abs(q14size.Width - q23size.Width);

            if (
                comboY.Count1 > 0
                && comboY.Count2 > 0
                && comboX.Count1 > 0
                && comboX.Count2 > 0
                && centerW > Tolerance.Epsilon
                && centerH > Tolerance.Epsilon
            )
            {
                CenterRemnant = new Box(
                    Bin.X + System.Math.Min(q14size.Length, q23size.Length),
                    Bin.Y + System.Math.Min(q14size.Width, q23size.Width),
                    centerW,
                    centerH
                );
            }
        }

        /// <summary>
        /// Keeps the least-waste combinations when their blocks can share the sheet, and otherwise
        /// or when a true pinwheel holds more, the pinwheel holding the most. The least-waste
        /// combinations often cannot: on a tight axis they make one pair of blocks the larger on
        /// both axes, so the spiral would overlap itself while a slacker one fits.
        /// </summary>
        private (CombinationResult Y, CombinationResult X) SelectCombinations(Item item)
        {
            var tightY = BestCombination.FindFrom2(item.Width, item.Length, Bin.Width);
            var tightX = BestCombination.FindFrom2(item.Length, item.Width, Bin.Length);

            if (!tightY.Found || !tightX.Found)
                return (null, null);

            var (bestY, bestX) = (default(CombinationResult), default(CombinationResult));
            var bestCount = AlternativeMustExceed;

            if (Fits(item, tightY, tightX))
            {
                (bestY, bestX) = (tightY, tightX);
                bestCount = System.Math.Max(bestCount, CountParts(item, tightY, tightX));
            }

            var alternativesX = BestCombination
                .AllFrom2(item.Length, item.Width, Bin.Length)
                .Where(c => c.Count1 > 0 && c.Count2 > 0)
                .ToList();

            foreach (var comboY in BestCombination.AllFrom2(item.Width, item.Length, Bin.Width))
            {
                if (comboY.Count1 == 0 || comboY.Count2 == 0)
                    continue;

                foreach (var comboX in alternativesX)
                {
                    if (!Fits(item, comboY, comboX))
                        continue;

                    var count = CountParts(item, comboY, comboX);
                    if (count > bestCount)
                        (bestY, bestX, bestCount) = (comboY, comboX, count);
                }
            }

            return (bestY, bestX);
        }

        private static Size Q14Size(Item item, CombinationResult comboY, CombinationResult comboX) =>
            new(item.Width * comboY.Count1, item.Length * comboX.Count1);

        private static Size Q23Size(Item item, CombinationResult comboY, CombinationResult comboX) =>
            new(item.Length * comboY.Count2, item.Width * comboX.Count2);

        // Q2 meets Q3, or Q1 meets Q4, only when one pair of blocks is the larger on both axes.
        private static bool Fits(Item item, CombinationResult comboY, CombinationResult comboX)
        {
            var q14size = Q14Size(item, comboY, comboX);
            var q23size = Q23Size(item, comboY, comboX);

            return !(
                (q14size.Width > q23size.Width && q14size.Length > q23size.Length)
                || (q23size.Width > q14size.Width && q23size.Length > q14size.Length)
            );
        }

        private static int CountParts(Item item, CombinationResult comboY, CombinationResult comboX)
        {
            var q14size = Q14Size(item, comboY, comboX);
            var q23size = Q23Size(item, comboY, comboX);
            var centerW = System.Math.Abs(q14size.Length - q23size.Length);
            var centerH = System.Math.Abs(q14size.Width - q23size.Width);

            var center = System.Math.Max(
                GridCount(centerW, item.Length) * GridCount(centerH, item.Width),
                GridCount(centerW, item.Width) * GridCount(centerH, item.Length)
            );

            return 2 * (comboY.Count1 * comboX.Count1 + comboY.Count2 * comboX.Count2) + center;
        }

        private static int GridCount(double span, double size) =>
            (int)System.Math.Floor((span + Tolerance.Epsilon) / size);
    }
}
