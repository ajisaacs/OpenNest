using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public enum PartClass
    {
        Large,
        Medium,
        Small,
    }

    public static class MultiPlateNester
    {
        public static List<NestItem> SortItems(List<NestItem> items, PartSortOrder sortOrder)
        {
            switch (sortOrder)
            {
                case PartSortOrder.BoundingBoxArea:
                    return items
                        .OrderByDescending(i =>
                        {
                            var bb = i.Drawing.Program.BoundingBox();
                            return bb.Width * bb.Length;
                        })
                        .ToList();

                case PartSortOrder.Size:
                    return items
                        .OrderByDescending(i =>
                        {
                            var bb = i.Drawing.Program.BoundingBox();
                            return System.Math.Max(bb.Width, bb.Length);
                        })
                        .ToList();

                default:
                    return items.ToList();
            }
        }

        public static PartClass Classify(Box partBounds, Box workArea)
        {
            var halfWidth = workArea.Width / 2.0;
            var halfLength = workArea.Length / 2.0;

            if (partBounds.Width > halfWidth || partBounds.Length > halfLength)
                return PartClass.Large;

            var workAreaArea = workArea.Width * workArea.Length;
            var partArea = partBounds.Width * partBounds.Length;

            if (partArea > workAreaArea / 9.0)
                return PartClass.Medium;

            return PartClass.Small;
        }

        public static bool IsScrapRemnant(Box remnant, double minRemnantSize)
        {
            return remnant.Width < minRemnantSize && remnant.Length < minRemnantSize;
        }

        public static List<Box> FindScrapZones(Plate plate, double minRemnantSize)
        {
            var finder = RemnantFinder.FromPlate(plate);
            var remnants = finder.FindRemnants();

            var scrap = new List<Box>();
            foreach (var remnant in remnants)
            {
                if (IsScrapRemnant(remnant, minRemnantSize))
                    scrap.Add(remnant);
            }

            return scrap;
        }

        public static List<Box> FindViableRemnants(Plate plate, double minRemnantSize)
        {
            var finder = RemnantFinder.FromPlate(plate);
            var remnants = finder.FindRemnants();

            var viable = new List<Box>();
            foreach (var remnant in remnants)
            {
                if (!IsScrapRemnant(remnant, minRemnantSize))
                    viable.Add(remnant);
            }

            return viable;
        }

        public struct UpgradeDecision
        {
            public bool ShouldUpgrade;
            public double UpgradeCost;
            public double NewPlateCost;
        }

        public static Plate CreatePlate(Plate template, List<PlateOption> options, Box minBounds)
        {
            var plate = new Plate(template.Size)
            {
                PartSpacing = template.PartSpacing,
                Quadrant = template.Quadrant,
            };
            plate.EdgeSpacing = new Spacing
            {
                Left = template.EdgeSpacing.Left,
                Right = template.EdgeSpacing.Right,
                Top = template.EdgeSpacing.Top,
                Bottom = template.EdgeSpacing.Bottom,
            };

            if (options == null || options.Count == 0 || minBounds == null)
                return plate;

            // Find smallest option that fits the part (by cost ascending).
            var sorted = options.OrderBy(o => o.Cost).ToList();

            foreach (var option in sorted)
            {
                var workW = option.Width - template.EdgeSpacing.Left - template.EdgeSpacing.Right;
                var workL = option.Length - template.EdgeSpacing.Top - template.EdgeSpacing.Bottom;

                var fitsNormal = workW >= minBounds.Width - Tolerance.Epsilon
                              && workL >= minBounds.Length - Tolerance.Epsilon;
                var fitsRotated = workW >= minBounds.Length - Tolerance.Epsilon
                               && workL >= minBounds.Width - Tolerance.Epsilon;

                if (fitsNormal || fitsRotated)
                {
                    plate.Size = new Size(option.Width, option.Length);
                    return plate;
                }
            }

            // No option fits — use template size.
            return plate;
        }

        public static UpgradeDecision EvaluateUpgradeVsNew(
            PlateOption currentSize,
            PlateOption upgradeSize,
            PlateOption newPlateSize,
            double salvageRate,
            double estimatedNewPlateUtilization)
        {
            var upgradeCost = upgradeSize.Cost - currentSize.Cost;

            var newPlateCost = newPlateSize.Cost;
            var remnantFraction = 1.0 - estimatedNewPlateUtilization;
            var salvageCredit = remnantFraction * newPlateSize.Cost * salvageRate;
            var netNewCost = newPlateCost - salvageCredit;

            return new UpgradeDecision
            {
                ShouldUpgrade = upgradeCost < netNewCost,
                UpgradeCost = upgradeCost,
                NewPlateCost = netNewCost,
            };
        }
    }
}
