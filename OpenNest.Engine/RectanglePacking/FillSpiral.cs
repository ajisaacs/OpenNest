using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.RectanglePacking
{
    internal class FillSpiral : FillEngine
    {
        public Box CenterRemnant { get; private set; }

        public FillSpiral(Bin bin)
            : base(bin)
        {
        }

        public override void Fill(Item item)
        {
            Fill(item, int.MaxValue);
        }

        public override void Fill(Item item, int maxCount)
        {
            if (item == null) return;

            // Width = Y axis, Length = X axis
            var comboY = BestCombination.FindFrom2(item.Width, item.Length, Bin.Width);
            var comboX = BestCombination.FindFrom2(item.Length, item.Width, Bin.Length);

            if (!comboY.Found || !comboX.Found)
                return;

            var q14size = new Size(
                item.Width * comboY.Count1,
                item.Length * comboX.Count1);
            var q23size = new Size(
                item.Length * comboY.Count2,
                item.Width * comboX.Count2);

            if ((q14size.Width > q23size.Width && q14size.Length > q23size.Length) ||
                (q23size.Width > q14size.Width && q23size.Length > q14size.Length))
                return; // cant do an efficient spiral fill

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
            item.Location = new Vector(
                Bin.X + q23size.Length,
                Bin.Y + q23size.Width);
            var q4 = FillGrid(item, comboY.Count1, comboX.Count1, maxCount);
            Bin.Items.AddRange(q4);

            // Compute center remnant — the rectangular gap between the 4 quadrants
            // Only valid when all 4 quadrants have items; otherwise the "center"
            // overlaps an occupied quadrant and recursion never terminates.
            var centerW = System.Math.Abs(q14size.Length - q23size.Length);
            var centerH = System.Math.Abs(q14size.Width - q23size.Width);

            if (comboY.Count1 > 0 && comboY.Count2 > 0 && comboX.Count1 > 0 && comboX.Count2 > 0
                && centerW > Tolerance.Epsilon && centerH > Tolerance.Epsilon)
            {
                CenterRemnant = new Box(
                    Bin.X + System.Math.Min(q14size.Length, q23size.Length),
                    Bin.Y + System.Math.Min(q14size.Width, q23size.Width),
                    centerW,
                    centerH);
            }
        }
    }
}
