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

            return FillHelpers.BestOverAngles(context, angles,
                angle =>
                {
                    var engine = new FillLinear(workArea, context.Plate.PartSpacing) { Label = "Linear" };
                    var result = FillHelpers.FillWithDirectionPreference(
                        dir => engine.Fill(context.Item.Drawing, angle, dir),
                        preferred, comparer, workArea);

                    if (result != null && result.Count > 0)
                    {
                        context.AngleResults.Add(new AngleResult
                        {
                            AngleDeg = Angle.ToDegrees(angle),
                            Direction = preferred ?? NestDirection.Horizontal,
                            PartCount = result.Count
                        });
                    }

                    return result;
                },
                NestPhase.Linear, "Linear");
        }
    }
}
