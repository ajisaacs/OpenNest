using OpenNest.Engine.Fill;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Engine.Strategies
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
            var comparer = context.Policy?.Comparer ?? new DefaultFillComparer();
            var preferred = context.Policy?.PreferredDirection;
            List<Part> best = null;

            for (var ai = 0; ai < angles.Count; ai++)
            {
                context.Token.ThrowIfCancellationRequested();

                var angle = angles[ai];
                var engine = new FillLinear(workArea, context.Plate.PartSpacing);

                var result = FillHelpers.FillWithDirectionPreference(
                    dir => engine.Fill(context.Item.Drawing, angle, dir),
                    preferred, comparer, workArea);

                var angleDeg = Angle.ToDegrees(angle);

                if (result != null && result.Count > 0)
                {
                    context.AngleResults.Add(new AngleResult
                    {
                        AngleDeg = angleDeg,
                        Direction = preferred ?? NestDirection.Horizontal,
                        PartCount = result.Count
                    });

                    if (best == null || comparer.IsBetter(result, best, workArea))
                        best = result;
                }

                NestEngineBase.ReportProgress(context.Progress, new ProgressReport
                {
                    Phase = NestPhase.Linear,
                    PlateNumber = context.PlateNumber,
                    Parts = best,
                    WorkArea = workArea,
                    Description = $"Linear: {ai + 1}/{angles.Count} angles, {angleDeg:F0}° best = {best?.Count ?? 0} parts",
                });
            }

            return best ?? new List<Part>();
        }
    }
}
