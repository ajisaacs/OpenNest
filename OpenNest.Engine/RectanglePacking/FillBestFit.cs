using OpenNest.Math;
using System;

namespace OpenNest.RectanglePacking
{
    internal class FillBestFit : FillEngine
    {
        public FillBestFit(Bin bin)
            : base(bin)
        {
        }

        public override void Fill(Item item)
        {
            var bin1 = BestFitHorizontal(item);
            var bin2 = BestFitVertical(item);

            if (bin1.Items.Count == bin2.Items.Count)
            {
                var usedArea1 = bin1.Items.GetBoundingBox().Area();
                var usedArea2 = bin2.Items.GetBoundingBox().Area();

                if (usedArea2 < usedArea1)
                    Bin.Items.AddRange(bin2.Items);
                else
                    Bin.Items.AddRange(bin1.Items);
            }
            else if (bin1.Items.Count > bin2.Items.Count)
                Bin.Items.AddRange(bin1.Items);
            else
                Bin.Items.AddRange(bin2.Items);
        }

        public override void Fill(Item item, int maxCount)
        {
            throw new NotImplementedException();
        }

        private Bin BestFitHorizontal(Item item) => BestFitAxis(item, horizontal: true);

        private Bin BestFitVertical(Item item) => BestFitAxis(item, horizontal: false);

        private Bin BestFitAxis(Item item, bool horizontal)
        {
            var bin = Bin.Clone() as Bin;

            var primarySize = horizontal ? item.Width : item.Length;
            var secondarySize = horizontal ? item.Length : item.Width;
            var binPrimary = horizontal ? bin.Width : Bin.Length;
            var binSecondary = horizontal ? bin.Length : Bin.Width;

            if (!BestCombination.FindFrom2(primarySize, secondarySize, binPrimary, out var normalPrimary, out var rotatePrimary))
                return bin;

            var normalSecondary = (int)System.Math.Floor((binSecondary + Tolerance.Epsilon) / secondarySize);
            var rotateSecondary = (int)System.Math.Floor((binSecondary + Tolerance.Epsilon) / primarySize);

            var (normalRows, normalCols) = horizontal
                ? (normalSecondary, normalPrimary)
                : (normalPrimary, normalSecondary);
            var (rotateRows, rotateCols) = horizontal
                ? (rotateSecondary, rotatePrimary)
                : (rotatePrimary, rotateSecondary);

            item.Location = bin.Location;

            bin.Items.AddRange(FillGrid(item, normalRows, normalCols, int.MaxValue));

            if (horizontal)
                item.Location.X += item.Width * normalPrimary;
            else
                item.Location.Y += item.Length * normalPrimary;

            item.Rotate();

            bin.Items.AddRange(FillGrid(item, rotateRows, rotateCols, int.MaxValue));

            return bin;
        }
    }
}
