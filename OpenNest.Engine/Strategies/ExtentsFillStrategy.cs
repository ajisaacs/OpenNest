using System.Collections.Generic;
using OpenNest.Engine.BestFit;
using OpenNest.Math;

namespace OpenNest
{
    public class ExtentsFillStrategy : IFillStrategy
    {
        public string Name => "Extents";
        public NestPhase Phase => NestPhase.Extents;
        public int Order => 300;

        public List<Part> Fill(FillContext context)
        {
            var filler = new FillExtents(context.WorkArea, context.Plate.PartSpacing);

            var bestRotation = context.SharedState.TryGetValue("BestRotation", out var rot)
                ? (double)rot
                : RotationAnalysis.FindBestRotation(context.Item);

            var angles = new[] { bestRotation, bestRotation + Angle.HalfPI };

            var bestFits = context.SharedState.TryGetValue("BestFits", out var cached)
                ? (List<BestFitResult>)cached
                : null;

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var angle in angles)
            {
                context.Token.ThrowIfCancellationRequested();
                var result = filler.Fill(context.Item.Drawing, angle,
                    context.PlateNumber, context.Token, context.Progress, bestFits);
                if (result != null && result.Count > 0)
                {
                    var score = FillScore.Compute(result, context.WorkArea);
                    if (best == null || score > bestScore)
                    {
                        best = result;
                        bestScore = score;
                    }
                }
            }

            return best ?? new List<Part>();
        }
    }
}
