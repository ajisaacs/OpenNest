using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest
{
    public class LinearFillStrategy : IFillStrategy
    {
        public string Name => "Linear";
        public NestPhase Phase => NestPhase.Linear;
        public int Order => 400;

        public List<Part> Fill(FillContext context)
        {
            var angles = context.SharedState.TryGetValue("AngleCandidates", out var cached)
                ? (List<double>)cached
                : new List<double> { 0, Angle.HalfPI };

            var workArea = context.WorkArea;
            List<Part> best = null;
            var bestScore = default(FillScore);

            for (var ai = 0; ai < angles.Count; ai++)
            {
                context.Token.ThrowIfCancellationRequested();

                var angle = angles[ai];
                var engine = new FillLinear(workArea, context.Plate.PartSpacing);
                var h = engine.Fill(context.Item.Drawing, angle, NestDirection.Horizontal);
                var v = engine.Fill(context.Item.Drawing, angle, NestDirection.Vertical);

                var angleDeg = Angle.ToDegrees(angle);

                if (h != null && h.Count > 0)
                {
                    var scoreH = FillScore.Compute(h, workArea);
                    context.AngleResults.Add(new AngleResult
                    {
                        AngleDeg = angleDeg,
                        Direction = NestDirection.Horizontal,
                        PartCount = h.Count
                    });

                    if (best == null || scoreH > bestScore)
                    {
                        best = h;
                        bestScore = scoreH;
                    }
                }

                if (v != null && v.Count > 0)
                {
                    var scoreV = FillScore.Compute(v, workArea);
                    context.AngleResults.Add(new AngleResult
                    {
                        AngleDeg = angleDeg,
                        Direction = NestDirection.Vertical,
                        PartCount = v.Count
                    });

                    if (best == null || scoreV > bestScore)
                    {
                        best = v;
                        bestScore = scoreV;
                    }
                }

                NestEngineBase.ReportProgress(context.Progress, NestPhase.Linear,
                    context.PlateNumber, best, workArea,
                    $"Linear: {ai + 1}/{angles.Count} angles, {angleDeg:F0}° best = {bestScore.Count} parts");
            }

            return best ?? new List<Part>();
        }
    }
}
