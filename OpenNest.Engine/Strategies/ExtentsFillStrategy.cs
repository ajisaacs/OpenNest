using System.Collections.Generic;
using OpenNest.Engine.Fill;
using OpenNest.Math;

namespace OpenNest.Engine.Strategies
{
    public class ExtentsFillStrategy : IFillStrategy
    {
        public string Name => "Extents";
        public NestPhase Phase => NestPhase.Extents;
        public int Order => 300;

        public List<Part> Fill(FillContext context)
        {
            if (context.PartType == PartType.Rectangle || context.PartType == PartType.Circle)
                return null;
            var filler = new FillExtents(context.WorkArea, context.Plate.PartSpacing);

            var bestRotation = context.SharedState.TryGetValue("BestRotation", out var rot)
                ? (double)rot
                : RotationAnalysis.FindBestRotation(context.Item);

            var angles = new[] { bestRotation, bestRotation + Angle.HalfPI };

            return FillHelpers.BestOverAngles(
                context,
                angles,
                angle =>
                    filler.Fill(context.Item.Drawing, angle, context.Token, context.ReportProgress),
                "Extents"
            );
        }
    }
}
