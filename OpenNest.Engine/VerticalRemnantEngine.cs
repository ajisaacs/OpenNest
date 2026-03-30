using System;
using System.Collections.Generic;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>
    /// Optimizes for the largest right-side vertical drop.
    /// Scores by count first, then minimizes X-extent.
    /// Prefers horizontal nest direction and angles that keep parts narrow in X.
    /// </summary>
    public class VerticalRemnantEngine : DefaultNestEngine
    {
        public VerticalRemnantEngine(Plate plate) : base(plate) { }

        public override string Name => "Vertical Remnant";

        public override string Description => "Optimizes for largest right-side vertical drop";

        protected override IFillComparer CreateComparer() => new VerticalRemnantComparer();

        public override NestDirection? PreferredDirection => NestDirection.Horizontal;

        public override List<double> BuildAngles(NestItem item, ClassificationResult classification, Box workArea)
        {
            var baseAngles = new List<double> { classification.PrimaryAngle, classification.PrimaryAngle + Angle.HalfPI };
            baseAngles.Sort((a, b) => RotatedWidth(item, a).CompareTo(RotatedWidth(item, b)));
            return baseAngles;
        }

        private static double RotatedWidth(NestItem item, double angle)
        {
            var bb = item.Drawing.Program.BoundingBox();
            var cos = System.Math.Abs(System.Math.Cos(angle));
            var sin = System.Math.Abs(System.Math.Sin(angle));
            return bb.Width * cos + bb.Length * sin;
        }
    }
}
