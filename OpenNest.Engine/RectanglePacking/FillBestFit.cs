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

        private Bin BestFitHorizontal(Item item)
        {
            var bin = Bin.Clone() as Bin;

            int normalColumns = 0;
            int rotateColumns = 0;

            if (!BestCombination.FindFrom2(item.Width, item.Length, bin.Width, out normalColumns, out rotateColumns))
                return bin;

            var normalRows = (int)System.Math.Floor((bin.Length + Tolerance.Epsilon) / item.Length);
            var rotateRows = (int)System.Math.Floor((bin.Length + Tolerance.Epsilon) / item.Width);

            item.Location = bin.Location;

            bin.Items.AddRange(FillGrid(item, normalRows, normalColumns, int.MaxValue));

            item.Location.X += item.Width * normalColumns;
            item.Rotate();

            bin.Items.AddRange(FillGrid(item, rotateRows, rotateColumns, int.MaxValue));

            return bin;
        }

        private Bin BestFitVertical(Item item)
        {
            var bin = Bin.Clone() as Bin;

            int normalRows = 0;
            int rotateRows = 0;

            if (!BestCombination.FindFrom2(item.Length, item.Width, Bin.Length, out normalRows, out rotateRows))
                return bin;

            var normalColumns = (int)System.Math.Floor((Bin.Width + Tolerance.Epsilon) / item.Width);
            var rotateColumns = (int)System.Math.Floor((Bin.Width + Tolerance.Epsilon) / item.Length);

            item.Location = bin.Location;

            bin.Items.AddRange(FillGrid(item, normalRows, normalColumns, int.MaxValue));

            item.Location.Y += item.Length * normalRows;
            item.Rotate();

            bin.Items.AddRange(FillGrid(item, rotateRows, rotateColumns, int.MaxValue));

            return bin;
        }
    }
}
