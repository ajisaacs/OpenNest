using System;
using System.Collections.Generic;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>
    /// Optimizes for the largest top-side horizontal drop.
    /// Scores by count first, then minimizes Y-extent.
    /// Prefers vertical nest direction and angles that keep parts narrow in Y.
    /// </summary>
    public class HorizontalRemnantEngine : DefaultNestEngine
    {
        public HorizontalRemnantEngine(Plate plate) : base(plate) { }

        public override string Name => "Horizontal Remnant";

        public override string Description => "Optimizes for largest top-side horizontal drop";

        protected override IFillComparer CreateComparer() => new HorizontalRemnantComparer();

        public override NestDirection? PreferredDirection => NestDirection.Vertical;

        public override ShrinkAxis TrimAxis => ShrinkAxis.Length;

        public override List<double> BuildAngles(NestItem item, ClassificationResult classification, Box workArea)
        {
            var baseAngles = new List<double> { classification.PrimaryAngle, classification.PrimaryAngle + Angle.HalfPI };
            baseAngles.Sort((a, b) => RotatedHeight(item, a).CompareTo(RotatedHeight(item, b)));
            return baseAngles;
        }

        private static double RotatedHeight(NestItem item, double angle)
        {
            var bb = item.Drawing.Program.BoundingBox();
            var cos = System.Math.Abs(System.Math.Cos(angle));
            var sin = System.Math.Abs(System.Math.Sin(angle));
            return bb.Length * cos + bb.Width * sin;
        }
    }
}
