using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit.Tiling
{
    public class TileEvaluator
    {
        public TileResult Evaluate(BestFitResult bestFit, Plate plate)
        {
            var plateWidth = plate.Size.Width - plate.EdgeSpacing.Left - plate.EdgeSpacing.Right;
            var plateHeight = plate.Size.Length - plate.EdgeSpacing.Top - plate.EdgeSpacing.Bottom;

            var result1 = TryTile(bestFit, plateWidth, plateHeight, false);
            var result2 = TryTile(bestFit, plateWidth, plateHeight, true);
            return result1.PartsNested >= result2.PartsNested ? result1 : result2;
        }

        private TileResult TryTile(BestFitResult bestFit, double plateWidth, double plateHeight, bool rotatePair)
        {
            var pairWidth = rotatePair ? bestFit.BoundingHeight : bestFit.BoundingWidth;
            var pairHeight = rotatePair ? bestFit.BoundingWidth : bestFit.BoundingHeight;
            var spacing = bestFit.Candidate.Spacing;

            var cols = (int)System.Math.Floor((plateWidth + spacing) / (pairWidth + spacing));
            var rows = (int)System.Math.Floor((plateHeight + spacing) / (pairHeight + spacing));
            var pairsNested = cols * rows;
            var partsNested = pairsNested * 2;

            var usedArea = partsNested * (bestFit.TrueArea / 2);
            var plateArea = plateWidth * plateHeight;

            var placements = new List<PairPlacement>();

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    placements.Add(new PairPlacement
                    {
                        Position = new Vector(
                            col * (pairWidth + spacing),
                            row * (pairHeight + spacing)),
                        PairRotation = rotatePair ? Angle.HalfPI : 0
                    });
                }
            }

            return new TileResult
            {
                BestFit = bestFit,
                PairsNested = pairsNested,
                PartsNested = partsNested,
                Rows = rows,
                Columns = cols,
                Utilization = plateArea > 0 ? usedArea / plateArea : 0,
                Placements = placements,
                PairRotated = rotatePair
            };
        }
    }
}
