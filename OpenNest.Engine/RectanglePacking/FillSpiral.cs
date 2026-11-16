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
            var spread = CenterSpread(item, comboY, comboX);

            // Q1: normal orientation at bin origin
            item.Location = Bin.Location;
            var q1 = FillGrid(item, comboY.Count1, comboX.Count1, maxCount);
            Bin.Items.AddRange(q1);

            // Q2: rotated, above Q1
            item.Rotate();
            item.Location = new Vector(Bin.X, Bin.Y + q14size.Width + spread.Y);
            var q2 = FillGrid(item, comboY.Count2, comboX.Count2, maxCount - Bin.Items.Count);
            Bin.Items.AddRange(q2);

            // Q3: rotated, right of Q1
            item.Location = new Vector(Bin.X + q14size.Length + spread.X, Bin.Y);
            var q3 = FillGrid(item, comboY.Count2, comboX.Count2, maxCount - Bin.Items.Count);
            Bin.Items.AddRange(q3);

            // Q4: normal orientation, diagonal from Q1
            item.Rotate();
            item.Location = new Vector(Bin.X + q23size.Length + spread.X, Bin.Y + q23size.Width + spread.Y);
            var q4 = FillGrid(item, comboY.Count1, comboX.Count1, maxCount);
            Bin.Items.AddRange(q4);

            // Compute center remnant — the rectangular gap between the 4 quadrants
            // Only valid when all 4 quadrants have items; otherwise the "center"
            // overlaps an occupied quadrant and recursion never terminates.
            if (HasCenter(comboY, comboX, q14size, q23size))
            {
                CenterRemnant = new Box(
                    Bin.X + System.Math.Min(q14size.Length, q23size.Length),
                    Bin.Y + System.Math.Min(q14size.Width, q23size.Width),
                    System.Math.Abs(q14size.Length - q23size.Length) + spread.X,
                    System.Math.Abs(q14size.Width - q23size.Width) + spread.Y
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

        private int CountParts(Item item, CombinationResult comboY, CombinationResult comboX) =>
            2 * (comboY.Count1 * comboX.Count1 + comboY.Count2 * comboX.Count2)
            + CenterSpread(item, comboY, comboX).Count;

        private static bool HasCenter(
            CombinationResult comboY,
            CombinationResult comboX,
            Size q14size,
            Size q23size
        ) =>
            comboY.Count1 > 0
            && comboY.Count2 > 0
            && comboX.Count1 > 0
            && comboX.Count2 > 0
            && System.Math.Abs(q14size.Length - q23size.Length) > Tolerance.Epsilon
            && System.Math.Abs(q14size.Width - q23size.Width) > Tolerance.Epsilon;

        /// <summary>
        /// How far to move the far blocks (Q3 and Q4 along X, Q2 and Q4 along Y) into the slack
        /// the spiral leaves at the bin's far edges, and the plain-grid count of the center that
        /// opens. Moving a pair of far blocks out widens the center by the same amount, so a
        /// center just short of a part can take one when the sheet has room to spare. The blocks
        /// move only as far as the best center count needs, keeping the rest of the slack at the
        /// far edges; with no gain they stay put.
        /// </summary>
        private (double X, double Y, int Count) CenterSpread(
            Item item,
            CombinationResult comboY,
            CombinationResult comboX
        )
        {
            var q14size = Q14Size(item, comboY, comboX);
            var q23size = Q23Size(item, comboY, comboX);

            if (!HasCenter(comboY, comboX, q14size, q23size))
                return (0, 0, 0);

            var centerW = System.Math.Abs(q14size.Length - q23size.Length);
            var centerH = System.Math.Abs(q14size.Width - q23size.Width);
            var slackX = System.Math.Max(0, Bin.Length - q14size.Length - q23size.Length);
            var slackY = System.Math.Max(0, Bin.Width - q14size.Width - q23size.Width);

            var best = (X: 0.0, Y: 0.0, Count: CenterCount(item, centerW, centerH));

            foreach (var (along, across) in new[] { (item.Length, item.Width), (item.Width, item.Length) })
            {
                var columns = GridCount(centerW + slackX, along);
                var rows = GridCount(centerH + slackY, across);
                var count = columns * rows;
                var x = System.Math.Max(0, columns * along - centerW);
                var y = System.Math.Max(0, rows * across - centerH);

                if (count > best.Count || (count == best.Count && count > 0 && x + y < best.X + best.Y))
                    best = (x, y, count);
            }

            return best;
        }

        private static int CenterCount(Item item, double centerW, double centerH) =>
            System.Math.Max(
                GridCount(centerW, item.Length) * GridCount(centerH, item.Width),
                GridCount(centerW, item.Width) * GridCount(centerH, item.Length)
            );

        private static int GridCount(double span, double size) =>
            (int)System.Math.Floor((span + Tolerance.Epsilon) / size);
    }
}
