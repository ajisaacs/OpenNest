using OpenNest.Geometry;

namespace OpenNest.RectanglePacking
{
    internal static class RectFill
    {
        public static void FillBest(Bin bin, Item item, int maxCount = int.MaxValue)
        {
            var spiralBin = bin.Clone() as Bin;
            var spiral = new FillSpiral(spiralBin);
            spiral.Fill(item, maxCount);

            // Recursively fill the center remnant of the spiral
            if (spiralBin.Items.Count > 0 && spiral.CenterRemnant != null)
            {
                var center = spiral.CenterRemnant;
                var fitsNormal = item.Length <= center.Length && item.Width <= center.Width;
                var fitsRotated = item.Width <= center.Length && item.Length <= center.Width;

                if (fitsNormal || fitsRotated)
                {
                    var remaining = maxCount - spiralBin.Items.Count;
                    FillBest(center.Location, center.Size, spiralBin, item, remaining);
                }
            }

            var bestFitBin = bin.Clone() as Bin;
            new FillBestFit(bestFitBin).Fill(item, maxCount);

            var winner = spiralBin.Items.Count >= bestFitBin.Items.Count ? spiralBin : bestFitBin;
            bin.Items.AddRange(winner.Items);
        }

        public static void FillBest(Vector location, Size size, Bin target, Item item, int maxCount)
        {
            if (size.Width <= 0 || size.Length <= 0 || maxCount <= 0)
                return;

            var bin = new Bin { Location = location, Size = size };
            FillBest(bin, item, maxCount);
            target.Items.AddRange(bin.Items);
        }
    }
}
